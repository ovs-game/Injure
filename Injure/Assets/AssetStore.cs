// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Injure.ModUtils;

namespace Injure.Assets;

/// <summary>
/// Opaque handle to a registered asset source, used for unregistration.
/// </summary>
public readonly struct AssetSourceHandle {
	internal readonly ulong ID;
	internal AssetSourceHandle(ulong id) {
		ID = id;
	}
}

/// <summary>
/// Opaque handle to a registered asset resolver, used for unregistration.
/// </summary>
public readonly struct AssetResolverHandle {
	internal readonly ulong ID;
	internal AssetResolverHandle(ulong id) {
		ID = id;
	}
}

/// <summary>
/// Opaque handle to a registered asset creator, used for unregistration.
/// </summary>
public readonly struct AssetCreatorHandle {
	internal readonly Type AssetType;
	internal readonly ulong ID;
	internal AssetCreatorHandle(Type assetType, ulong id) {
		AssetType = assetType;
		ID = id;
	}
}

/// <summary>
/// Opaque handle to a registered asset dependency watcher, used for unregistration.
/// </summary>
public readonly struct AssetDependencyWatcherHandle {
	internal readonly Type AssetType;
	internal readonly ulong ID;
	internal AssetDependencyWatcherHandle(Type assetType, ulong id) {
		AssetType = assetType;
		ID = id;
	}
}

/// <summary>
/// Central manager of assets, as well as their sources, resolvers, and creators,
/// live versions, and queued reload publication.
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="AssetRef{T}"/> refers to a stable slot in this store. A slot
/// may have a current live version, a prepared replacement version queued for
/// publication, or neither.
/// </para>
/// <para>
/// Reload publication is explicit: <see cref="AssetRef{T}.QueueReloadAsync(CancellationToken)"/>
/// prepares a replacement version, and <see cref="ApplyQueuedReloads()"/> publishes
/// all queued replacements. Borrowing an asset never applies a queued reload.
/// </para>
/// <para>
/// Threads may attach to the store with <see cref="AttachCurrentThread()"/> and
/// report quiescent points with <see cref="AssetThreadContext.AtSafeBoundary()"/> (or the convenience
/// method <see cref="AtSafeBoundary()"/>). When reloads are published, previously live versions are
/// not reclaimed immediately; instead, the reclamation is deferred until every attached thread
/// has passed a safe boundary after that publication. In other words, call <see cref="AtSafeBoundary()"/>
/// when you're okay with the <see cref="AssetRef{T}"/>s you're using getting updated and old leases
/// getting invalidated. This does NOT mean that you must report a safe boundary just to
/// observe newly published reloads.
/// </para>
/// <para>
/// Threads that are not attached to the store are outside that guarantee; they may
/// still borrow/warm/etc. assets, but a <see cref="ApplyQueuedReloads()"/> call may
/// invalidate currently borrowed leases.
/// </para>
/// <para>
/// Published asset versions also carry a dependency list collected during the
/// creation of the asset. Registered dependency watchers can observe those dependencies
/// and automatically queue reloads when the underlying external resources change.
/// </para>
/// </remarks>
public sealed class AssetStore {
	// ==========================================================================
	// versions/slots
	private sealed class PendingPrepared(IUntypedAssetCreator creator, AssetPreparedData prepared, ImmutableArray<IAssetDependency> deps, ulong version) {
		public readonly IUntypedAssetCreator Creator = creator;
		public readonly AssetPreparedData Prepared = prepared;
		public readonly ImmutableArray<IAssetDependency> Dependencies = deps;
		public readonly ulong Version = version;
	}

	private sealed class PendingRetired(object val, ulong epoch) {
		public readonly object Val = val;
		public readonly ulong RetireEpoch = epoch;
	}

	internal interface IAssetSlot {
		AssetStore Store { get; }
		Type AssetType { get; }
		AssetID AssetID { get; }
		bool HasCurrent { get; }
		bool HasQueuedReload { get; }
		Exception? LastException { get; }

		Task WarmAsync(CancellationToken ct);
		Task QueueReloadAsync(CancellationToken ct);

		// returns true if something actually got published
		bool ApplyQueuedReload(ulong epoch);
	}

	internal sealed class AssetVersion<T>(T value, ulong version, ImmutableArray<IAssetDependency> deps) where T : class {
		public readonly T Value = value;
		public readonly ulong Version = version;
		public readonly ImmutableArray<IAssetDependency> Dependencies = deps;
	}

	internal sealed class AssetSlot<T>(ulong slotID, AssetStore store, AssetID id) : IAssetSlot where T : class {
		private readonly Lock @lock = new Lock();

		private AssetVersion<T>? curr;
		private PendingPrepared? pending;

		private Task? prepareTask;
		private Task? materializeTask;

		private Exception? lastEx;
		private ulong nextver = 1;

		public readonly ulong SlotID = slotID;
		public AssetStore Store { get; } = store;
		public Type AssetType => typeof(T);
		public AssetID AssetID { get; } = id;

		public bool HasCurrent => Volatile.Read(ref curr) is not null;
		internal bool HasPendingPrepared => Volatile.Read(ref pending) is not null;
		public bool HasQueuedReload {
			get {
				lock (@lock)
					return curr is not null && pending is not null;
			}
		}
		public Exception? LastException => Volatile.Read(ref lastEx);

		public AssetLease<T> Borrow() {
			// if `curr` exists, return it immediately
			// if `curr` doesn't exist, this method is allowed to synchronously materialize the first version
			AssetVersion<T>? ver = Volatile.Read(ref curr);
			if (ver is not null)
				return new AssetLease<T>(ver.Value, ver.Version, ver.Dependencies.AsSpan());

			// block until it exists
			Task waitTask = getOrStartMaterialize();
			waitTask.GetAwaiter().GetResult();
			if (waitTask.IsFaulted)
				throw waitTask.Exception;
			ver = Volatile.Read(ref curr) ?? throw new InternalStateException("asset materialize finished without making the curr version");
			return new AssetLease<T>(ver.Value, ver.Version, ver.Dependencies.AsSpan());
		}

		public bool TryPassiveBorrow(out AssetLease<T> lease) {
			AssetVersion<T>? ver = Volatile.Read(ref curr);
			lease = (ver is not null) ? new AssetLease<T>(ver.Value, ver.Version, ver.Dependencies.AsSpan()) : default;
			return ver is not null;
		}

		public async Task WarmAsync(CancellationToken ct) {
			if (Volatile.Read(ref curr) is not null)
				return;
			await getOrStartMaterialize().WaitAsync(ct).ConfigureAwait(false);
			if (Volatile.Read(ref curr) is null)
				throw new InternalStateException("asset materialize finished without making the curr version");
		}

		public async Task QueueReloadAsync(CancellationToken ct) {
			// if there's nothing to replace that's just a warm
			if (Volatile.Read(ref curr) is null) {
				await WarmAsync(ct).ConfigureAwait(false);
				return;
			}

			Task waitTask;
			TaskCompletionSource<bool>? startPrepare = null;
			lock (@lock) {
				// if we already have a prepared reload, don't queue another one on top
				if (pending is not null)
					return;
				if (prepareTask is not null) {
					// if we waited here from inside the same load chain it'd deadlock
					checkCycle(new AssetKey(AssetID, typeof(T)));
				} else {
					startPrepare = new TaskCompletionSource<bool>(
						TaskCreationOptions.RunContinuationsAsynchronously
					);
					prepareTask = startPrepare.Task;
				}
				waitTask = prepareTask;
			}
			if (startPrepare is not null)
				_ = runPrepareAsync(startPrepare, reload: true);
			await waitTask.WaitAsync(ct).ConfigureAwait(false);
			if (waitTask.IsCompletedSuccessfully && Volatile.Read(ref pending) is null)
				throw new InternalStateException("queueing a reload finished without actually queueing a reload");
		}

		private async Task runPrepareAsync(TaskCompletionSource<bool> tcs, bool reload) {
			IUntypedAssetCreator creator;
			AssetPreparedData prepared;
			ImmutableArray<IAssetDependency> deps;
			try {
				(creator, prepared, deps) = await Store.tryPrepareValueAsync<T>(AssetID, CancellationToken.None).ConfigureAwait(false);
			} catch (Exception caught) {
				lock (@lock) {
					prepareTask = null;
					lastEx = caught;
				}
				tcs.SetException(caught);
				return;
			}

			lock (@lock) {
				prepareTask = null;
				lastEx = null;
				if (curr is not null && !reload) {
					// background warm finished after something else already did
					// a sync load, no reason to put this up for reload
					(prepared as IDisposable)?.Dispose();
				} else {
					(pending?.Prepared as IDisposable)?.Dispose();
					pending = new PendingPrepared(creator, prepared, deps, nextver++);
				}
			}
			tcs.SetResult(true);
		}

		private async Task runMaterializeAsync(TaskCompletionSource<bool> tcs) {
			try {
				for (;;) {
					PendingPrepared? pprep = null;
					Task? waitPrepare = null;
					TaskCompletionSource<bool>? startPrepare = null;
					lock (@lock) {
						if (curr is not null) {
							materializeTask = null;
							tcs.SetResult(true);
							return;
						}
						if (pending is not null) {
							pprep = pending;
							pending = null;
						} else if (prepareTask is not null) {
							waitPrepare = prepareTask;
						} else {
							startPrepare = new TaskCompletionSource<bool>(
								TaskCreationOptions.RunContinuationsAsynchronously
							);
							prepareTask = startPrepare.Task;
							waitPrepare = prepareTask;
						}
					}
					if (startPrepare is not null)
						_ = runPrepareAsync(startPrepare, reload: false);
					if (waitPrepare is not null) {
						await waitPrepare.ConfigureAwait(false);
						continue;
					}
					if (pprep is null)
						throw new InternalStateException("expected to have a prepared asset by this point");

					// at this point `curr is null` and we exclusively own the
					// prepared asset, finalize it outside the lock
					T newval = finalizePrepared<T>(AssetID, pprep);
					AssetVersion<T> newver = new AssetVersion<T>(newval, pprep.Version, pprep.Dependencies);

					lock (@lock) {
						if (curr is null) {
							curr = newver;
							lastEx = null;
							Store.onSlotPublished(this, ImmutableArray<IAssetDependency>.Empty, newver.Dependencies);
						} else {
							// another thread beat us while we were waiting for the lock
							(newver.Value as IDisposable)?.Dispose();
						}
						materializeTask = null;
						tcs.SetResult(true);
					}
					(pprep.Prepared as IDisposable)?.Dispose();
					return;
				}
			} catch (Exception caught) {
				lock (@lock) {
					lastEx = caught;
					materializeTask = null;
				}
				tcs.SetException(caught);
			}
		}

		private Task getOrStartMaterialize() {
			Task waitTask;
			TaskCompletionSource<bool>? startMaterialize = null;
			lock (@lock) {
				// if we already have a materialized curr version don't replace it
				if (curr is not null)
					return Task.CompletedTask;
				if (materializeTask is not null) {
					// if we waited here from inside the same load chain it'd deadlock
					checkCycle(new AssetKey(AssetID, typeof(T)));
				} else {
					startMaterialize = new TaskCompletionSource<bool>(
						TaskCreationOptions.RunContinuationsAsynchronously
					);
					materializeTask = startMaterialize.Task;
				}
				waitTask = materializeTask;
			}
			if (startMaterialize is not null)
				_ = runMaterializeAsync(startMaterialize);
			return waitTask;
		}

		public bool ApplyQueuedReload(ulong epoch) {
			PendingPrepared pprep;
			lock (@lock) {
				if (pending is null)
					return false;
				pprep = pending;
				pending = null;
			}

			AssetVersion<T> newver;
			try {
				T newval = finalizePrepared<T>(AssetID, pprep);
				newver = new AssetVersion<T>(newval, pprep.Version, pprep.Dependencies);
			} catch (Exception caught) {
				lock (@lock)
					lastEx = caught;
				(pprep.Prepared as IDisposable)?.Dispose();
				return false;
			}

			AssetVersion<T>? oldver;
			lock (@lock) {
				oldver = curr;
				curr = newver;
				lastEx = null;
				Store.onSlotPublished(this, oldver?.Dependencies ?? ImmutableArray<IAssetDependency>.Empty, newver.Dependencies);
			}

			(pprep.Prepared as IDisposable)?.Dispose();
#pragma warning disable IDE0001 // name can be simplified
			if (oldver is not null)
				Store.QueueRetire<T>(oldver.Value, epoch);
#pragma warning restore IDE0001 // name can be simplified
			return true;
		}
	}

	// ==========================================================================
	// creation pipeline boilerplate wrappers
	private interface ISourceEntry {
		Task<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDependencyCollector coll, CancellationToken ct);
	}
	private sealed class SyncSourceEntry(IAssetSource source) : ISourceEntry {
		private readonly IAssetSource source = source;
		public Task<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
			ct.ThrowIfCancellationRequested();
			return Task.FromResult(source.TrySource(info, coll));
		}
	}
	private sealed class AsyncSourceEntry(IAssetSourceAsync source) : ISourceEntry {
		private readonly IAssetSourceAsync source = source;
		public Task<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDependencyCollector coll, CancellationToken ct) =>
			source.TrySourceAsync(info, coll, ct);
	}

	private interface IResolverEntry {
		Task<AssetResolveResult> TryResolveAsync(AssetResolveAsyncInfo info, IAssetDependencyCollector coll, CancellationToken ct);
	}
	private sealed class SyncResolverEntry(IAssetResolver resolver) : IResolverEntry {
		private readonly IAssetResolver resolver = resolver;
		public Task<AssetResolveResult> TryResolveAsync(AssetResolveAsyncInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
			ct.ThrowIfCancellationRequested();
			AssetResolveInfo syncInfo = new AssetResolveInfo(info.AssetID,
				(AssetID id) => info.FetchAsync(id, ct).GetAwaiter().GetResult());
			return Task.FromResult(resolver.TryResolve(syncInfo, coll));
		}
	}
	private sealed class AsyncResolverEntry(IAssetResolverAsync resolver) : IResolverEntry {
		private readonly IAssetResolverAsync resolver = resolver;
		public Task<AssetResolveResult> TryResolveAsync(AssetResolveAsyncInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
			return resolver.TryResolveAsync(info, coll, ct);
		}
	}

	private interface IUntypedAssetCreator {
		Type AssetType { get; }
		Task<AssetCreatePreparedResult> TryCreateAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct);
		AssetCreateResult<object> TryFinalize(AssetFinalizeInfo info);
	}
	private sealed class UntypedAssetCreator<T>(IAssetCreatorAsync<T> creator) : IUntypedAssetCreator where T : class {
		private readonly IAssetCreatorAsync<T> creator = creator;
		public Type AssetType => typeof(T);
		public Task<AssetCreatePreparedResult> TryCreateAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
			return creator.TryCreateAsync(info, coll, ct);
		}
		public AssetCreateResult<object> TryFinalize(AssetFinalizeInfo info) {
			AssetCreateResult<T> result = creator.TryFinalize(info);
			return result.Kind switch {
				AssetCreateResultKind.NotHandled => AssetCreateResult<object>.NotHandled(),
				AssetCreateResultKind.Success => AssetCreateResult<object>.Success(result.Value!), // null will be handled later
				AssetCreateResultKind.Error => AssetCreateResult<object>.Error(result.Exception!), // null will be handled later
				_ => throw new UnreachableException()
			};
		}
	}

	// ==========================================================================
	// dependency tracking (incl. boilerplate wrappers)
	private sealed class DependencyCollector : IAssetDependencyCollector {
		private readonly HashSet<IAssetDependency> deps = new();
		public void Add(IAssetDependency dependency) {
			ArgumentNullException.ThrowIfNull(dependency);
			deps.Add(dependency);
		}
		public ImmutableArray<IAssetDependency> Snapshot() => [.. deps];
		public void MergeFrom(DependencyCollector child) {
			foreach (IAssetDependency dep in child.deps)
				deps.Add(dep);
		}
	}

	private interface IUntypedAssetDependencyWatcher : IDisposable {
		Type DependencyType { get; }
		void Watch(IAssetDependency dependency);
		void Unwatch(IAssetDependency dependency);
		event Action<IAssetDependency> Changed;
	}

	private sealed class UntypedAssetDependencyWatcher<T> : IUntypedAssetDependencyWatcher where T : IAssetDependency {
		private readonly IAssetDependencyWatcher<T> inner;

		public Type DependencyType => typeof(T);
		public event Action<IAssetDependency>? Changed;

		public UntypedAssetDependencyWatcher(IAssetDependencyWatcher<T> inner) {
			this.inner = inner;
			this.inner.Changed += onChanged;
		}
		private void onChanged(T dependency) => Changed?.Invoke(dependency);

		public void Watch(IAssetDependency dependency) => inner.Watch((T)dependency);
		public void Unwatch(IAssetDependency dependency) => inner.Unwatch((T)dependency);
		public void Dispose() {
			inner.Changed -= onChanged;
			inner.Dispose();
		}
	}

	// ==========================================================================
	// load cycle tracking
	private sealed class AssetLoadStackFrame(AssetKey key, AssetLoadStackFrame? prev) {
		public readonly AssetKey Key = key;
		public readonly AssetLoadStackFrame? Prev = prev;
	}
	private static readonly AsyncLocal<AssetLoadStackFrame> loadStackTop = new AsyncLocal<AssetLoadStackFrame>();

	// ==========================================================================
	// general bookkeeping
	private readonly ConcurrentDictionary<AssetKey, IAssetSlot> slots = new ConcurrentDictionary<AssetKey, IAssetSlot>();
	private ulong nextSlotID = 0; // first ID will be 1 since this gets incremented upfront

	// ==========================================================================
	// creation pipeline bookkeeping
	private readonly Lock registryLock = new Lock();
	private readonly OwnerOrderedRegistry<ISourceEntry> sources = new OwnerOrderedRegistry<ISourceEntry>();
	private readonly OwnerOrderedRegistry<IResolverEntry> resolvers = new OwnerOrderedRegistry<IResolverEntry>();
	private ImmutableDictionary<Type, OwnerOrderedRegistry<IUntypedAssetCreator>> creators = ImmutableDictionary<Type, OwnerOrderedRegistry<IUntypedAssetCreator>>.Empty;

	// ==========================================================================
	// publication / reclamation / thread context bookkeeping
	private readonly ConcurrentQueue<PendingRetired> retired = new ConcurrentQueue<PendingRetired>();
	private ulong publishedEpoch = 0;
	internal ulong GetPublishedEpoch() => Volatile.Read(ref publishedEpoch);

	[ThreadStatic] private static Dictionary<AssetStore, AssetThreadContext>? tlsContexts;
	private readonly ConcurrentDictionary<ulong, AssetThreadContext> attachedContexts = new ConcurrentDictionary<ulong, AssetThreadContext>();

	// ==========================================================================
	// dependency bookkeeping
	private readonly Lock dependencyLock = new Lock();
	private Dictionary<Type, OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>> watchers = new Dictionary<Type, OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>>();
	private readonly Dictionary<IAssetDependency, HashSet<IAssetSlot>> slotsByDependency = new Dictionary<IAssetDependency, HashSet<IAssetSlot>>();

	// ==========================================================================
	// public api

	/// <summary>
	/// Gets a stable handle for the specified asset.
	/// </summary>
	/// <typeparam name="T">Expected asset type. Determines what creators are tried.</typeparam>
	/// <param name="id">Asset ID.</param>
	/// <returns>An <see cref="AssetRef{T}"/> handle for the specified asset.
	public AssetRef<T> GetAsset<T>(AssetID id) where T : class {
		AssetKey key = new AssetKey(id, typeof(T));
		if (!slots.TryGetValue(key, out IAssetSlot? s)) {
			IAssetSlot @new = new AssetSlot<T>(Interlocked.Increment(ref nextSlotID), this, id);
			s = slots.GetOrAdd(key, @new);
		}
		return new AssetRef<T>((AssetSlot<T>)s);
	}

	/// <summary>
	/// Attaches the current thread to this <see cref="AssetStore"/> for deferred
	/// asset version reclamation tracking.
	/// </summary>
	/// <returns>
	/// An <see cref="AssetThreadContext"/> object representing this thread's participation
	/// in this store's safe-boundary / deferred-reclaim model.
	/// </returns>
	/// <remarks>
	/// Attachment is per-thread and per-store. A single thread may be attached to
	/// multiple <see cref="AssetStore"/> instances at the same time.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the current thread is already attached to this store.
	/// </exception>
	public AssetThreadContext AttachCurrentThread() {
		AssetThreadContext ctx = new AssetThreadContext(this);
		tlsContexts ??= new Dictionary<AssetStore, AssetThreadContext>(ReferenceEqualityComparer.Instance);
		if (tlsContexts.ContainsKey(this))
			throw new InvalidOperationException("current thread is already attached to this AssetStore");
		if (!attachedContexts.TryAdd(ctx.ID, ctx))
			throw new InternalStateException("duplicate asset thread context id");
		tlsContexts.Add(this, ctx);
		Volatile.Write(ref ctx.QuiescentEpoch, Volatile.Read(ref publishedEpoch));
		return ctx;
	}

	/// <summary>
	/// Reports a safe boundary for the current thread's attached context in this store.
	/// </summary>
	/// <remarks>
	/// This method is a convenience wrapper over <see cref="AssetThreadContext.AtSafeBoundary()"/>
	/// for the current thread's attached context in this store.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the current thread is not attached to this store.
	/// </exception>
	public void AtSafeBoundary() {
		if (tlsContexts is null || !tlsContexts.TryGetValue(this, out AssetThreadContext? ctx))
			throw new InvalidOperationException("the current thread is not attached to this AssetStore. if you're using Task/etc., your code is not guaranteed to run on the same physical thread across an await boundary, use a real Thread");
		ctx.AtSafeBoundary();
	}

	/// <summary>
	/// Finalizes and applies all queued asset reloads.
	/// </summary>
	/// <returns>
	/// The number of asset slots that published a new live version.
	/// </returns>
	/// <remarks>
	/// Publication is immediate; subsequent borrows after a call observe the new
	/// value. Previously live values are kept alive until every attached thread
	/// reports a safe boundary, at which point they are reclaimed.
	/// </remarks>
	public int ApplyQueuedReloads() {
		ulong epoch = Interlocked.Increment(ref publishedEpoch);
		int n = 0;
		foreach (IAssetSlot s in slots.Values.ToArray())
			if (s.ApplyQueuedReload(epoch))
				n++;
		TryCollectRetired();
		return n;
	}

	/// <summary>
	/// Registers a non-async asset source.
	/// </summary>
	/// <param name="ownerID">Owner ID to register the source under.</param>
	/// <param name="source">Source to register.</param>
	/// <param name="localID">Local ID for the source, used for deterministic ordering and tie-breaking.</param>
	/// <param name="localPriority">Owner-local priority; higher-priority sources within the same owner are tried first.</param>
	/// <param name="beforeOwners">
	/// If not <see langword="null"/>, this source will be tried before any sources registered
	/// under one of the specified owner IDs.
	/// </param>
	/// <param name="afterOwners">
	/// If not <see langword="null"/>, this source will only be tried after all sources registered
	/// under one of the specified owner IDs.
	/// </param>
	/// <returns>
	/// An opaque handle that can be passed to <see cref="UnregisterSource(AssetSourceHandle)"/>.
	/// </returns>
	/// <exception cref="OwnerOrderingException">
	/// Thrown if the new ordering constraints are invalid or unsatisfiable.
	/// </exception>
	public AssetSourceHandle RegisterSource(string ownerID, IAssetSource source, string localID,
		int localPriority = 0, IEnumerable<string>? beforeOwners = null, IEnumerable<string>? afterOwners = null) {
		ArgumentNullException.ThrowIfNull(source);
		lock (registryLock) {
			ulong id = sources.Register(new OwnerOrderedEntry<ISourceEntry>(
				new SyncSourceEntry(source),
				ownerID, localID, localPriority, beforeOwners, afterOwners
			));
			return new AssetSourceHandle(id);
		}
	}

	/// <summary>
	/// Registers an async asset source.
	/// </summary>
	/// <param name="ownerID">Owner ID to register the source under.</param>
	/// <param name="source">Source to register.</param>
	/// <param name="localID">Local ID for the source, used for deterministic ordering and tie-breaking.</param>
	/// <param name="localPriority">Owner-local priority; higher-priority sources within the same owner are tried first.</param>
	/// <param name="beforeOwners">
	/// If not <see langword="null"/>, this source will be tried before any sources registered
	/// under one of the specified owner IDs.
	/// </param>
	/// <param name="afterOwners">
	/// If not <see langword="null"/>, this source will only be tried after all sources registered
	/// under one of the specified owner IDs.
	/// </param>
	/// <returns>
	/// An opaque handle that can be passed to <see cref="UnregisterSource(AssetSourceHandle)"/>.
	/// </returns>
	/// <remarks>
	/// <paramref name="localID"/> must be unique among all other sources registered in
	/// this <see cref="AssetStore"/> instance under this <paramref name="ownerID"/>.
	/// </remarks>
	/// <exception cref="OwnerOrderingException">
	/// Thrown if the new ordering constraints are invalid or unsatisfiable.
	/// </exception>
	public AssetSourceHandle RegisterSource(string ownerID, IAssetSourceAsync source, string localID,
		int localPriority = 0, IEnumerable<string>? beforeOwners = null, IEnumerable<string>? afterOwners = null) {
		ArgumentNullException.ThrowIfNull(source);
		lock (registryLock) {
			ulong id = sources.Register(new OwnerOrderedEntry<ISourceEntry>(
				new AsyncSourceEntry(source),
				ownerID, localID, localPriority, beforeOwners, afterOwners
			));
			return new AssetSourceHandle(id);
		}
	}

	/// <summary>
	/// Registers a non-async asset resolver.
	/// </summary>
	/// <param name="ownerID">Owner ID to register the resolver under.</param>
	/// <param name="resolver">Resolver to register.</param>
	/// <param name="localID">Local ID for the resolver, used for deterministic ordering and tie-breaking.</param>
	/// <param name="localPriority">Owner-local priority; higher-priority resolvers within the same owner are tried first.</param>
	/// <param name="beforeOwners">
	/// If not <see langword="null"/>, this resolver will be tried before any resolvers registered
	/// under one of the specified owner IDs.
	/// </param>
	/// <param name="afterOwners">
	/// If not <see langword="null"/>, this resolver will only be tried after all resolvers registered
	/// under one of the specified owner IDs.
	/// </param>
	/// <returns>
	/// An opaque handle that can be passed to <see cref="UnregisterResolver(AssetResolverHandle)"/>.
	/// </returns>
	/// <remarks>
	/// <paramref name="localID"/> must be unique among all other resolvers registered in
	/// this <see cref="AssetStore"/> instance under this <paramref name="ownerID"/>.
	/// </remarks>
	/// <exception cref="OwnerOrderingException">
	/// Thrown if the new ordering constraints are invalid or unsatisfiable.
	/// </exception>
	public AssetResolverHandle RegisterResolver(string ownerID, IAssetResolver resolver, string localID,
		int localPriority = 0, IEnumerable<string>? beforeOwners = null, IEnumerable<string>? afterOwners = null) {
		ArgumentNullException.ThrowIfNull(resolver);
		lock (registryLock) {
			ulong id = resolvers.Register(new OwnerOrderedEntry<IResolverEntry>(
				new SyncResolverEntry(resolver),
				ownerID, localID, localPriority, beforeOwners, afterOwners
			));
			return new AssetResolverHandle(id);
		}
	}

	/// <summary>
	/// Registers an async asset resolver.
	/// </summary>
	/// <param name="ownerID">Owner ID to register the resolver under.</param>
	/// <param name="resolver">Resolver to register.</param>
	/// <param name="localID">Local ID for the resolver, used for deterministic ordering and tie-breaking.</param>
	/// <param name="localPriority">Owner-local priority; higher-priority resolvers within the same owner are tried first.</param>
	/// <param name="beforeOwners">
	/// If not <see langword="null"/>, this resolver will be tried before any resolvers registered
	/// under one of the specified owner IDs.
	/// </param>
	/// <param name="afterOwners">
	/// If not <see langword="null"/>, this resolver will only be tried after all resolvers registered
	/// under one of the specified owner IDs.
	/// </param>
	/// <returns>
	/// An opaque handle that can be passed to <see cref="UnregisterResolver(AssetResolverHandle)"/>.
	/// </returns>
	/// <remarks>
	/// <paramref name="localID"/> must be unique among all other resolvers registered in
	/// this <see cref="AssetStore"/> instance under this <paramref name="ownerID"/>.
	/// </remarks>
	/// <exception cref="OwnerOrderingException">
	/// Thrown if the new ordering constraints are invalid or unsatisfiable.
	/// </exception>
	public AssetResolverHandle RegisterResolver(string ownerID, IAssetResolverAsync resolver, string localID,
		int localPriority = 0, IEnumerable<string>? beforeOwners = null, IEnumerable<string>? afterOwners = null) {
		ArgumentNullException.ThrowIfNull(resolver);
		lock (registryLock) {
			ulong id = resolvers.Register(new OwnerOrderedEntry<IResolverEntry>(
				new AsyncResolverEntry(resolver),
				ownerID, localID, localPriority, beforeOwners, afterOwners
			));
			return new AssetResolverHandle(id);
		}
	}

	/// <summary>
	/// Registers an asset creator of a specific asset type.
	/// </summary>
	/// <typeparam name="T">Asset type produced by the creator.</typeparam>
	/// <param name="ownerID">Owner ID to register the creator under.</param>
	/// <param name="creator">Creator to register.</param>
	/// <param name="localID">Local ID for the creator, used for deterministic ordering and tie-breaking.</param>
	/// <param name="localPriority">Owner-local priority; higher-priority creators within the same owner are tried first.</param>
	/// <param name="beforeOwners">
	/// If not <see langword="null"/>, this creator will be tried before any creators registered
	/// under one of the specified owner IDs.
	/// </param>
	/// <param name="afterOwners">
	/// If not <see langword="null"/>, this creator will only be tried after all creators registered
	/// under one of the specified owner IDs.
	/// </param>
	/// <returns>
	/// An opaque handle that can be passed to <see cref="UnregisterCreator(AssetCreatorHandle)"/>.
	/// </returns>
	/// <remarks>
	/// <para>
	/// <paramref name="localID"/> must be unique among all other creators for the type <typeparamref name="T"/>
	/// registered in this <see cref="AssetStore"/> instance under this <paramref name="ownerID"/>.
	/// </para>
	/// <para>
	/// Non-async creators are not supported.
	/// </para>
	/// </remarks>
	/// <exception cref="OwnerOrderingException">
	/// Thrown if the new ordering constraints are invalid or unsatisfiable.
	/// </exception>
	public AssetCreatorHandle RegisterCreator<T>(string ownerID, IAssetCreatorAsync<T> creator, string localID,
		int localPriority = 0, IEnumerable<string>? beforeOwners = null, IEnumerable<string>? afterOwners = null)
		where T : class {
		ArgumentNullException.ThrowIfNull(creator);
		lock (registryLock) {
			OwnerOrderedEntry<IUntypedAssetCreator> ent = new OwnerOrderedEntry<IUntypedAssetCreator>(
				new UntypedAssetCreator<T>(creator),
				ownerID, localID, localPriority, beforeOwners, afterOwners
			);
			ImmutableDictionary<Type, OwnerOrderedRegistry<IUntypedAssetCreator>> old = creators;
			ulong id;
			if (old.TryGetValue(typeof(T), out OwnerOrderedRegistry<IUntypedAssetCreator>? reg)) {
				id = reg.Register(ent);
			} else {
				reg = new OwnerOrderedRegistry<IUntypedAssetCreator>();
				id = reg.Register(ent);
				Volatile.Write(ref creators, old.Add(typeof(T), reg));
			}
			return new AssetCreatorHandle(typeof(T), id);
		}
	}

	/// <summary>
	/// Registers a watcher for a specific dependency type.
	/// </summary>
	/// <typeparam name="T">Dependency type handled by the watcher.</typeparam>
	/// <param name="ownerID">Owner ID to register the watcher under.</param>
	/// <param name="watcher">Watcher to register.</param>
	/// <param name="localID">Local ID for the watcher, used for deterministic ordering and tie-breaking.</param>
	/// <param name="localPriority">
	/// Owner-local priority; higher-priority watchers within the same owner will be subscribed to new
	/// dependencies first.
	/// </param>
	/// <param name="beforeOwners">
	/// If not <see langword="null"/>, this watcher will be subscribed to new dependencies before
	/// any of the watchers registered under one of the specified owner IDs (also see remarks on
	/// unsubscribe order).
	/// </param>
	/// <param name="afterOwners">
	/// If not <see langword="null"/>, this watcher will be subscribed to new dependencies only after
	/// all of the watchers registered under one of the specified owner IDs (also see remarks on
	/// unsubscribe order).
	/// </param>
	/// <returns>
	/// An opaque handle that can be passed to <see cref="UnregisterDependencyWatcher(AssetDependencyWatcherHandle)"/>.
	/// </returns>
	/// <remarks>
	/// <para>
	/// Newly registered watchers are immediately subscribed to all currently published
	/// dependencies of the matching type that are already known to this store.
	/// </para>
	/// <para>
	/// Unsubscription is done in reverse order; the last-ordered watcher, i.e the last one to have
	/// been subscribed to a new dependency, will also be the first to be unsubscribed from it.
	/// </para>
	/// </remarks>
	/// <exception cref="OwnerOrderingException">
	/// Thrown if the new ordering constraints are invalid or unsatisfiable.
	/// </exception>
	public AssetDependencyWatcherHandle RegisterDependencyWatcher<T>(string ownerID, IAssetDependencyWatcher<T> watcher, string localID,
		int localPriority = 0, IEnumerable<string>? beforeOwners = null, IEnumerable<string>? afterOwners = null)
		where T : IAssetDependency {
		ArgumentNullException.ThrowIfNull(watcher);
		UntypedAssetDependencyWatcher<T> untyped = new UntypedAssetDependencyWatcher<T>(watcher);
		lock (dependencyLock) {
			OwnerOrderedEntry<IUntypedAssetDependencyWatcher> ent = new OwnerOrderedEntry<IUntypedAssetDependencyWatcher>(
				untyped,
				ownerID, localID, localPriority, beforeOwners, afterOwners
			);
			Dictionary<Type, OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>> old = watchers;
			ulong id;
			if (old.TryGetValue(typeof(T), out OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>? reg)) {
				id = reg.Register(ent);
			} else {
				reg = new OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>();
				id = reg.Register(ent);
				Dictionary<Type, OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>> @new = new Dictionary<Type, OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>>(old) {
					[typeof(T)] = reg
				};
				Volatile.Write(ref watchers, @new);
				untyped.Changed += onDependencyChanged;
				foreach ((IAssetDependency dep, _) in slotsByDependency)
					if (dep is T d)
						watcher.Watch(d);
			}
			return new AssetDependencyWatcherHandle(typeof(T), id);
		}
	}

	/// <summary>
	/// Unregisters an asset source.
	/// </summary>
	/// <param name="handle">The handle obtained from registration.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if the handle is invalid.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the handle doesn't point to a registered source, typically
	/// because it has already been unregistered.
	/// </exception>
	public void UnregisterSource(AssetSourceHandle handle) {
		if (handle.ID == 0)
			throw new ArgumentException("invalid handle (did you accidentally pass `default`?)", nameof(handle));
		lock (registryLock) {
			if (!sources.Unregister(handle.ID, out _))
				throw new InvalidOperationException("this handle doesn't point to a registered source");
		}
	}

	/// <summary>
	/// Unregisters an asset resolver.
	/// </summary>
	/// <param name="handle">The handle obtained from registration.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if the handle is invalid.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the handle doesn't point to a registered resolver, typically
	/// because it has already been unregistered.
	/// </exception>
	public void UnregisterResolver(AssetResolverHandle handle) {
		if (handle.ID == 0)
			throw new ArgumentException("invalid handle (did you accidentally pass `default`?)", nameof(handle));
		lock (registryLock) {
			if (!resolvers.Unregister(handle.ID, out _))
				throw new InvalidOperationException("this handle doesn't point to a registered resolver");
		}
	}

	/// <summary>
	/// Unregisters an asset creator.
	/// </summary>
	/// <param name="handle">The handle obtained from registration.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if the handle is invalid.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the handle doesn't point to a registered creator, typically
	/// because it has already been unregistered.
	/// </exception>
	public void UnregisterCreator(AssetCreatorHandle handle) {
		if (handle.AssetType is null || handle.ID == 0)
			throw new ArgumentException("invalid handle (did you accidentally pass `default`?)", nameof(handle));
		lock (registryLock) {
			if (!creators.TryGetValue(handle.AssetType, out OwnerOrderedRegistry<IUntypedAssetCreator>? reg))
				throw new InternalStateException("this handle's AssetType doesn't have a corresponding OwnerOrderedRegistry of creators, how did it even get handed out?");
			if (!reg.Unregister(handle.ID, out _))
				throw new InvalidOperationException("this handle doesn't point to a registered creator");
		}
	}

	/// <summary>
	/// Unregisters an asset dependency watcher.
	/// </summary>
	/// <param name="handle">The handle obtained from registration.</param>
	/// <remarks>
	/// Also calls <see cref="IDisposable.Dispose()"/> on the watcher.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if the handle is invalid.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the handle doesn't point to a registered watcher, typically
	/// because it has already been unregistered.
	/// </exception>
	public void UnregisterDependencyWatcher(AssetDependencyWatcherHandle handle) {
		if (handle.AssetType is null || handle.ID == 0)
			throw new ArgumentException("invalid handle (did you accidentally pass `default`?)", nameof(handle));
		lock (dependencyLock) {
			if (!watchers.TryGetValue(handle.AssetType, out OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>? reg))
				throw new InternalStateException("this handle's AssetType doesn't have a corresponding OwnerOrderedRegistry of watchers, how did it even get handed out?");
			if (!reg.Unregister(handle.ID, out OwnerOrderedEntry<IUntypedAssetDependencyWatcher>? ent))
				throw new InvalidOperationException("this handle doesn't point to a registered watcher");
			ent.Item.Dispose();
		}
	}

	// ==========================================================================
	// internal api
	internal void DetachThread(AssetThreadContext ctx) {
		attachedContexts.TryRemove(ctx.ID, out _);
		if (tlsContexts is null || !tlsContexts.Remove(this))
			throw new InvalidOperationException("tried to detach a thread that is not attached to this AssetStore. if you're using Task/etc., your code is not guaranteed to run on the same physical thread across an await boundary, use a real Thread");
	}

	internal void QueueRetire<T>(T val, ulong epoch) where T : class {
		retired.Enqueue(new PendingRetired(val, epoch));
	}

	internal void TryCollectRetired() {
		ulong cutoff = ulong.MaxValue;
		foreach (AssetThreadContext ctx in attachedContexts.Values) {
			ulong e = Volatile.Read(ref ctx.QuiescentEpoch);
			cutoff = Math.Min(e, cutoff);
		}
		while (retired.TryPeek(out PendingRetired? rv) && rv.RetireEpoch <= cutoff) {
			if (!retired.TryDequeue(out rv))
				break;
			(rv.Val as IRevokable)?.Revoke();
			(rv.Val as IDisposable)?.Dispose();
		}
	}

	// ==========================================================================
	// asset creation
	private static void checkCycle(AssetKey key) {
		AssetLoadStackFrame? prev = loadStackTop.Value;
		for (AssetLoadStackFrame? f = prev; f is not null; f = f.Prev)
			if (f.Key == key)
				throw makeCycleException(key, prev!);
	}

	private async Task<(IUntypedAssetCreator Creator, AssetPreparedData Prepared, ImmutableArray<IAssetDependency> Dependencies)>
		tryPrepareValueAsync<T>(AssetID id, CancellationToken ct) where T : class {
		AssetKey key = new AssetKey(id, typeof(T));
		AssetLoadStackFrame? prev = loadStackTop.Value;
		for (AssetLoadStackFrame? f = prev; f is not null; f = f.Prev)
			if (f.Key == key)
				throw makeCycleException(key, prev!);
		loadStackTop.Value = new AssetLoadStackFrame(key, prev);

		try {
			DependencyCollector rootColl = new DependencyCollector();
			AssetData data = await tryAllResolversAsync(id, rootColl, typeof(T), ct).ConfigureAwait(false);
			AssetCreateInfo info = new AssetCreateInfo(id, data);
			ImmutableDictionary<Type, OwnerOrderedRegistry<IUntypedAssetCreator>> dictsnap = Volatile.Read(ref creators);
			if (dictsnap.TryGetValue(typeof(T), out OwnerOrderedRegistry<IUntypedAssetCreator>? reg)) {
				IReadOnlyList<IUntypedAssetCreator> snapshot = reg.ReadSnapshot();
				foreach (IUntypedAssetCreator creator in snapshot) {
					DependencyCollector childColl = new DependencyCollector();
					AssetCreatePreparedResult res = await creator.TryCreateAsync(info, childColl, ct).ConfigureAwait(false);
					switch (res.Kind) {
					case AssetCreateResultKind.NotHandled:
						continue;
					case AssetCreateResultKind.Success:
						if (res.Prepared is null)
							throw new AssetLoadException(id, typeof(T), "asset creator prepare returned Success but didn't set Prepared");
						rootColl.MergeFrom(childColl);
						return (creator, res.Prepared, rootColl.Snapshot());
					case AssetCreateResultKind.Error:
						throw res.Exception ?? new AssetLoadException(id, typeof(T), "asset creator prepare returned Error with no exception attached");
					default:
						throw new UnreachableException();
					}
				}
			}
			throw new AssetUnhandledException(id, typeof(T), "no registered asset creator accepted the asset");
		} finally {
			if (prev is not null)
				loadStackTop.Value = prev;
		}
	}

	private static T finalizePrepared<T>(AssetID id, PendingPrepared pprep) where T : class {
		AssetFinalizeInfo info = new AssetFinalizeInfo(id, pprep.Prepared);
		AssetCreateResult<object> res = pprep.Creator.TryFinalize(info);
		return res.Kind switch {
			AssetCreateResultKind.NotHandled => throw new AssetLoadException(id, typeof(T), "prepared asset was unexpectedly not handled by the creator during finalize"),
			AssetCreateResultKind.Success => (T)(res.Value ?? throw new AssetLoadException(id, typeof(T), "asset creator finalize returned Success but didn't set Value")),
			AssetCreateResultKind.Error => throw res.Exception ?? new AssetLoadException(id, typeof(T), "asset creator finalize returned Error with no exception attached"),
			_ => throw new UnreachableException()
		};
	}

	// ==========================================================================
	// the rest of the asset pipeline
	private static async Task<Stream> fixstream(Stream stream, CancellationToken ct) {
		if (stream.CanSeek)
			return stream;
		MemoryStream ms = new MemoryStream();
		await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
		ms.Position = 0;
		stream.Dispose();
		return ms;
	}

	private async Task<Stream> tryAllSourcesAsync(AssetID id, DependencyCollector parentColl, Type t, CancellationToken ct) {
		IReadOnlyList<ISourceEntry> snapshot = sources.ReadSnapshot();
		AssetSourceInfo info = new AssetSourceInfo(id);
		foreach (ISourceEntry ent in snapshot) {
			DependencyCollector childColl = new DependencyCollector();
			AssetSourceResult res = await ent.TrySourceAsync(info, childColl, ct).ConfigureAwait(false);
			switch (res.Kind) {
			case AssetSourceResultKind.NotHandled:
				continue;
			case AssetSourceResultKind.Success:
				if (res.Stream is null)
					throw new AssetLoadException(id, t, "asset source returned Success but didn't set Stream");
				parentColl.MergeFrom(childColl);
				return await fixstream(res.Stream, ct).ConfigureAwait(false);
			case AssetSourceResultKind.Error:
				throw res.Exception ?? new AssetLoadException(id, t, "asset source returned Error with no exception attached");
			default:
				throw new UnreachableException();
			}
		}
		throw new AssetUnhandledException(id, t, "no registered asset source managed to provide the asset");
	}

	private async Task<AssetData> tryAllResolversAsync(AssetID id, DependencyCollector parentColl, Type t, CancellationToken ct) {
		IReadOnlyList<IResolverEntry> snapshot = resolvers.ReadSnapshot();
		foreach (IResolverEntry ent in snapshot) {
			DependencyCollector childColl = new DependencyCollector();
			AssetResolveAsyncInfo info = new AssetResolveAsyncInfo(id,
				(AssetID toFetch, CancellationToken passedCt) => tryAllSourcesAsync(toFetch, childColl, t, passedCt));
			AssetResolveResult res = await ent.TryResolveAsync(info, childColl, ct).ConfigureAwait(false);
			switch (res.Kind) {
			case AssetResolveResultKind.NotHandled:
				continue;
			case AssetResolveResultKind.Success:
				if (res.Data is null)
					throw new AssetLoadException(id, t, "asset resolver returned Success but didn't set Data");
				parentColl.MergeFrom(childColl);
				return res.Data;
			case AssetResolveResultKind.Error:
				throw res.Exception ?? new AssetLoadException(id, t, "asset resolver returned Error with no exception attached");
			default:
				throw new UnreachableException();
			}
		}
		throw new AssetUnhandledException(id, t, "no registered asset resolver managed to resolve the asset");
	}

	// ==========================================================================
	// dependencies
	private void onDependencyChanged(IAssetDependency dependency) {
		HashSet<IAssetSlot> slots;
		lock (dependencyLock) {
			if (!slotsByDependency.TryGetValue(dependency, out slots!))
				return;
		}
		foreach (IAssetSlot slot in slots)
			_ = slot.QueueReloadAsync(CancellationToken.None);
	}

	private void onSlotPublished(IAssetSlot slot, ImmutableArray<IAssetDependency> oldDeps, ImmutableArray<IAssetDependency> newDeps) {
		static HashSet<IAssetDependency> makeSet(ImmutableArray<IAssetDependency> deps) {
			HashSet<IAssetDependency> s = new HashSet<IAssetDependency>();
			for (int i = 0; i < deps.Length; i++)
				s.Add(deps[i]);
			return s;
		}

		// note: do the watch/unwatch calls under the lock, because the alternative
		// is a race condition window where the map says the dep is watched but
		// it actually isn't so an update is gonna get missed if it happens right then
		lock (dependencyLock) {
			HashSet<IAssetDependency> oldSet = makeSet(oldDeps);
			HashSet<IAssetDependency> newSet = makeSet(newDeps);
			foreach (IAssetDependency dep in oldSet) {
				if (newSet.Contains(dep))
					continue;
				if (!slotsByDependency.TryGetValue(dep, out HashSet<IAssetSlot>? users))
					continue;
				users.Remove(slot);
				if (users.Count != 0)
					continue;
				slotsByDependency.Remove(dep);
				if (watchers.TryGetValue(dep.GetType(), out OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>? reg)) {
					IReadOnlyList<IUntypedAssetDependencyWatcher> snapshot = reg.ReadSnapshot();
					for (int i = snapshot.Count - 1; i >= 0; i--) {
						IUntypedAssetDependencyWatcher watcher = snapshot[i];
						watcher.Unwatch(dep);
					}
				}
			}
			foreach (IAssetDependency dep in newSet) {
				if (oldSet.Contains(dep))
					continue;
				if (!slotsByDependency.TryGetValue(dep, out HashSet<IAssetSlot>? users)) {
					users = new HashSet<IAssetSlot>();
					slotsByDependency.Add(dep, users);
					if (watchers.TryGetValue(dep.GetType(), out OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>? reg)) {
						IReadOnlyList<IUntypedAssetDependencyWatcher> snapshot = reg.ReadSnapshot();
						foreach (IUntypedAssetDependencyWatcher watcher in snapshot)
							watcher.Watch(dep);
					}
				}
				users.Add(slot);
			}
		}
	}

	// ==========================================================================
	// exception formatting
	private static string formatCycle(List<AssetKey> cycle) {
		StringBuilder sb = new StringBuilder("recursive asset load detected: ");
		for (int i = 0; i < cycle.Count; i++) {
			if (i > 0)
				sb.Append(" -> ");
			sb.Append(cycle[i].AssetType.Name).Append('(').Append(cycle[i].AssetID).Append(')');
		}
		return sb.ToString();
	}

	private static AssetLoadCycleException makeCycleException(AssetKey repeated, AssetLoadStackFrame prev) {
		List<AssetKey> cycle = new List<AssetKey>();
		for (AssetLoadStackFrame? f = prev; f is not null; f = f.Prev) {
			cycle.Add(f.Key);
			if (f.Key == repeated)
				break;
		}
		cycle.Reverse();
		cycle.Add(repeated);
		return new AssetLoadCycleException(repeated.AssetID, repeated.AssetType, formatCycle(cycle), cycle);
	}
}
