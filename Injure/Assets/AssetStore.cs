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

using Injure.DataStructures;
using Injure.ModUtils;

namespace Injure.Assets;

/// <summary>
/// Opaque handle to a registered asset source, used for unregistration.
/// </summary>
public readonly struct AssetSourceHandle {
	internal readonly ulong StoreID;
	internal readonly ulong ID;
	internal AssetSourceHandle(ulong storeID, ulong id) {
		StoreID = storeID;
		ID = id;
	}
}

/// <summary>
/// Opaque handle to a registered asset resolver, used for unregistration.
/// </summary>
public readonly struct AssetResolverHandle {
	internal readonly ulong StoreID;
	internal readonly ulong ID;
	internal AssetResolverHandle(ulong storeID, ulong id) {
		StoreID = storeID;
		ID = id;
	}
}

/// <summary>
/// Opaque handle to a registered asset creator, used for unregistration.
/// </summary>
public readonly struct AssetCreatorHandle {
	internal readonly ulong StoreID;
	internal readonly Type AssetType;
	internal readonly ulong ID;
	internal AssetCreatorHandle(ulong storeID, Type assetType, ulong id) {
		StoreID = storeID;
		AssetType = assetType;
		ID = id;
	}
}

/// <summary>
/// Opaque handle to a registered asset dependency watcher, used for unregistration.
/// </summary>
public readonly struct AssetDependencyWatcherHandle {
	internal readonly ulong StoreID;
	internal readonly Type DependencyType;
	internal readonly ulong ID;
	internal AssetDependencyWatcherHandle(ulong storeID, Type dependencyType, ulong id) {
		StoreID = storeID;
		DependencyType = dependencyType;
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
	private interface IPendingAssetValue : IDisposable {
		object FinalizeValue(AssetID id);
	}

	private sealed class PendingPrepared(IPendingAssetValue prepared, ImmutableArray<IAssetDependency> deps, ulong version,
		AssetReloadRequestOrigin origin, IAssetDependency? trigger) : IDisposable {
		public readonly IPendingAssetValue Prepared = prepared;
		public readonly ImmutableArray<IAssetDependency> Dependencies = deps;
		public readonly ulong Version = version;
		public readonly AssetReloadRequestOrigin Origin = origin;
		public readonly IAssetDependency? Trigger = trigger;
		public void Dispose() => Prepared.Dispose();
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
		Exception? LastLoadException { get; }
		AssetReloadFailure? LastReloadFailure { get; }

		Task WarmAsync(CancellationToken ct);
		Task QueueReloadAsync(CancellationToken ct);
		void QueueReloadFromDependency(IAssetDependency dep);

		// returns true if something actually got published
		bool ApplyQueuedReload(ulong epoch, ImmutableArray<AssetReloadFailure>.Builder failures);
	}

	internal sealed class AssetVersion<T>(T value, ulong version, ImmutableArray<IAssetDependency> deps) where T : class {
		public readonly T Value = value;
		public readonly ulong Version = version;
		public readonly ImmutableArray<IAssetDependency> Dependencies = deps;
	}

	internal sealed class AssetSlot<T>(ulong slotID, AssetStore store, AssetID id) : IAssetSlot where T : class {
		private readonly Lock @lock = new();

		private AssetVersion<T>? curr;
		private PendingPrepared? pending;

		private bool reloadPrepareTaskActive;
		private TaskCompletionSource<object?> reloadStateChanged = new(
			TaskCreationOptions.RunContinuationsAsynchronously
		);
		private Task? materializeTask;

		private Exception? lastLoadException;
		private AssetReloadFailure? lastReloadFailure;
		private Exception? reloadPrepareTaskEx;

		private ulong newestRequestedVersion = 1;
		private AssetReloadRequestOrigin newestReloadOrigin;
		private IAssetDependency? newestReloadTrigger;

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
		public Exception? LastLoadException => Volatile.Read(ref lastLoadException);
		public AssetReloadFailure? LastReloadFailure => Volatile.Read(ref lastReloadFailure);

		public AssetLease<T> Borrow() {
			// if `curr` exists, return it immediately
			// if `curr` doesn't exist, this method is allowed to synchronously materialize the first version
			AssetVersion<T>? ver = Volatile.Read(ref curr);
			if (ver is not null)
				return new AssetLease<T>(ver.Value, ver.Version, ver.Dependencies.AsSpan());

			// block until it exists
			Task waitTask = getOrStartMaterialize();
			waitTask.GetAwaiter().GetResult();
			ver = Volatile.Read(ref curr) ?? throw new InternalStateException("asset materialize finished without making the curr version");
			return new AssetLease<T>(ver.Value, ver.Version, ver.Dependencies.AsSpan());
		}

		public bool TryPassiveBorrow(out AssetLease<T> lease) {
			AssetVersion<T>? ver = Volatile.Read(ref curr);
			lease = (ver is not null) ? new AssetLease<T>(ver.Value, ver.Version, ver.Dependencies.AsSpan()) : default;
			return ver is not null;
		}

		public async Task WarmAsync(CancellationToken ct) {
			ct.ThrowIfCancellationRequested();
			if (Volatile.Read(ref curr) is not null)
				return;
			await getOrStartMaterialize().WaitAsync(ct).ConfigureAwait(false);
			if (Volatile.Read(ref curr) is null)
				throw new InternalStateException("asset materialize finished without making the curr version");
		}

		public async Task QueueReloadAsync(CancellationToken ct) {
			// this is public
			ct.ThrowIfCancellationRequested();

			ulong targetVersion = 1;
			bool startTask = false;
			lock (@lock) {
				if (curr is not null)
					startTask = requestReloadLocked(AssetReloadRequestOrigin.Explicit, null, out targetVersion);
			}

			if (targetVersion == 1 && Volatile.Read(ref curr) is null) {
				await WarmAsync(ct).ConfigureAwait(false);
				return;
			}

			if (startTask)
				_ = runReloadPrepareLoopAsync();

			for (;;) {
				Task waitTask;

				lock (@lock) {
					if (reloadPrepareTaskEx is not null)
						throw reloadPrepareTaskEx;
					if (pending is not null && pending.Version >= targetVersion)
						return;
					if (curr is not null && curr.Version >= targetVersion)
						return;
					if (lastReloadFailure is not null && lastReloadFailure.TargetVersion >= targetVersion)
						throw lastReloadFailure.Exception;
					waitTask = reloadStateChanged.Task;
				}

				await waitTask.WaitAsync(ct).ConfigureAwait(false);
			}
		}

		public void QueueReloadFromDependency(IAssetDependency dep) {
			// this is internal
			bool startTask;
			lock (@lock)
				startTask = requestReloadLocked(AssetReloadRequestOrigin.Dependency, dep, out _);
			if (startTask)
				_ = runReloadPrepareLoopAsync();
		}

		public bool ApplyQueuedReload(ulong epoch, ImmutableArray<AssetReloadFailure>.Builder failures) {
			PendingPrepared pprep;
			lock (@lock) {
				if (pending is null)
					return false;
				if (pending.Version != newestRequestedVersion)
					throw new InternalStateException("stale pending reload was left publishable");
				pprep = pending;
				pending = null;
			}

			AssetVersion<T> newver;
			try {
				T newval = finalizePrepared<T>(AssetID, pprep.Prepared);
				newver = new AssetVersion<T>(newval, pprep.Version, pprep.Dependencies);
			} catch (Exception caught) {
				pprep.Dispose();
				AssetReloadFailure f = new(
					new AssetKey(AssetID, typeof(T)),
					pprep.Version,
					AssetReloadFailureStage.Finalize,
					pprep.Origin,
					pprep.Trigger,
					caught
				);
				lock (@lock) {
					lastReloadFailure = f;
					pulseReloadStateChangedLocked();
				}
				failures.Add(f);
				Store.RecordFailedReload(f);
				return false;
			}

			AssetVersion<T>? oldver;
			lock (@lock) {
				oldver = curr;
				curr = newver;
				lastReloadFailure = null;
				Store.onSlotPublished(this, oldver?.Dependencies ?? ImmutableArray<IAssetDependency>.Empty, newver.Dependencies);
			}

			pprep.Dispose();
#pragma warning disable IDE0001 // name can be simplified
			if (oldver is not null)
				Store.QueueRetire<T>(oldver.Value, epoch);
#pragma warning restore IDE0001 // name can be simplified
			return true;
		}

		private void pulseReloadStateChangedLocked() {
			TaskCompletionSource<object?> old = reloadStateChanged;
			reloadStateChanged = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously
			);
			old.TrySetResult(null);
		}

		private bool requestReloadLocked(AssetReloadRequestOrigin origin, IAssetDependency? trigger, out ulong targetVersion) {
			if (curr is null) {
				targetVersion = 1;
				return false;
			}

			targetVersion = ++newestRequestedVersion;
			newestReloadOrigin = origin;
			newestReloadTrigger = trigger;
			pending?.Dispose();
			pending = null;

			bool startTask = !reloadPrepareTaskActive;
			if (startTask) {
				if (reloadPrepareTaskEx is not null)
					throw reloadPrepareTaskEx;
				reloadPrepareTaskActive = true;
			}
			pulseReloadStateChangedLocked();
			return startTask;
		}

		private async Task runReloadPrepareLoopAsync() {
			try {
				for (;;) {
					ulong targetVersion;
					AssetReloadRequestOrigin targetOrigin;
					IAssetDependency? targetTrigger;

					lock (@lock) {
						if (curr is null)
							return;
						targetVersion = newestRequestedVersion;
						targetOrigin = newestReloadOrigin;
						targetTrigger = newestReloadTrigger;
					}

					PendingPrepared prepared;
					try {
						(IPendingAssetValue pendingAssetValue, ImmutableArray<IAssetDependency> deps) =
							await Store.tryPrepareValueAsync<T>(AssetID).ConfigureAwait(false);
						prepared = new PendingPrepared(pendingAssetValue, deps, targetVersion, targetOrigin, targetTrigger);
					} catch (Exception caught) when (caught is not InternalStateException) {
						lock (@lock) {
							if (targetVersion == newestRequestedVersion) {
								AssetReloadFailure f = new(
									new AssetKey(AssetID, typeof(T)),
									targetVersion,
									AssetReloadFailureStage.Prepare,
									targetOrigin,
									targetTrigger,
									caught
								);
								lastReloadFailure = f;
								reloadPrepareTaskActive = false;
								Store.RecordFailedReload(f);
								pulseReloadStateChangedLocked();
								return;
							}
						}
						continue;
					}

					lock (@lock) {
						if (targetVersion == newestRequestedVersion) {
							pending?.Dispose();
							pending = prepared;
							lastReloadFailure = null;
							reloadPrepareTaskActive = false;
							pulseReloadStateChangedLocked();
							return;
						}
					}
					prepared.Dispose();
				}
			} catch (Exception caught) {
				lock (@lock) {
					reloadPrepareTaskEx = caught;
					reloadPrepareTaskActive = false;
					pulseReloadStateChangedLocked();
				}
			}
		}

		private async Task runMaterializeAsync(TaskCompletionSource<object?> tcs) {
			IPendingAssetValue? pendingValue = null;
			T? newval = null;
			try {
				(pendingValue, ImmutableArray<IAssetDependency> deps) =
					await Store.tryPrepareValueAsync<T>(AssetID).ConfigureAwait(false);
				newval = finalizePrepared<T>(AssetID, pendingValue);
				AssetVersion<T> newver = new(newval, version: 1, deps);

				lock (@lock) {
					if (curr is null) {
						curr = newver;
						lastLoadException = null;
						materializeTask = null;
						newval = null;
						Store.onSlotPublished(this, ImmutableArray<IAssetDependency>.Empty, newver.Dependencies);
					} else {
						materializeTask = null;
					}
					tcs.SetResult(null);
				}
			} catch (Exception caught) {
				lock (@lock) {
					lastLoadException = caught;
					materializeTask = null;
					tcs.SetException(caught);
				}
			} finally {
				pendingValue?.Dispose();
				(newval as IDisposable)?.Dispose();
			}
		}

		private Task getOrStartMaterialize() {
			lock (@lock) {
				Task task = getOrStartMaterializeLocked(out TaskCompletionSource<object?>? startMaterialize);
				if (startMaterialize is not null)
					_ = runMaterializeAsync(startMaterialize);
				return task;
			}
		}

		private Task getOrStartMaterializeLocked(out TaskCompletionSource<object?>? startMaterialize) {
			startMaterialize = null;

			if (curr is not null)
				return Task.CompletedTask;
			if (materializeTask is not null) {
				// if we waited here from inside the same load chain it'd deadlock
				checkCycle(new AssetKey(AssetID, typeof(T)));
				return materializeTask;
			}
			startMaterialize = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously
			);
			materializeTask = startMaterialize.Task;
			return materializeTask;
		}
	}

	// ==========================================================================
	// creation pipeline boilerplate wrappers
	private sealed class DirectPendingAssetValue<T>(T val) : IPendingAssetValue where T : class {
		private T? val = val;

		public object FinalizeValue(AssetID id) {
			T ret = val ?? throw new InternalStateException("direct asset value finalized twice");
			val = null;
			return ret;
		}

		public void Dispose() {
			(val as IDisposable)?.Dispose();
			val = null;
		}
	}

	private sealed class StagedPendingAssetValue<T, TPrepared>(IAssetStagedCreator<T, TPrepared> creator, TPrepared prepared)
		: IPendingAssetValue where T : class where TPrepared : AssetPreparedData {
		private readonly IAssetStagedCreator<T, TPrepared> creator = creator;
		private TPrepared? prepared = prepared;

		public object FinalizeValue(AssetID id) {
			TPrepared p = prepared ?? throw new InternalStateException("staged asset value finalized twice");
			prepared = null;
			try {
				return creator.Finalize(new AssetFinalizeInfo<TPrepared>(id, p));
			} finally {
				p.Dispose();
			}
		}

		public void Dispose() {
			(prepared as IDisposable)?.Dispose();
			prepared = null;
		}
	}

	private readonly record struct UntypedCreateResult(
		AssetCreateResultKind Kind,
		IPendingAssetValue? Value = null
	) {
		public static UntypedCreateResult NotHandled() => new(AssetCreateResultKind.NotHandled);
		public static UntypedCreateResult Success(IPendingAssetValue value) => new(AssetCreateResultKind.Success, value);
	}

	private interface IUntypedAssetCreator {
		Type AssetType { get; }
		ValueTask<UntypedCreateResult> TryCreateAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct);
	}

	private sealed class UntypedDirectAssetCreator<T>(IAssetCreator<T> creator) : IUntypedAssetCreator where T : class {
		private readonly IAssetCreator<T> creator = creator;
		public Type AssetType => typeof(T);
		public async ValueTask<UntypedCreateResult> TryCreateAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
			AssetCreateResult<T> result = await creator.TryCreateAsync(info, coll, ct).ConfigureAwait(false);
			return result.Kind.Tag switch {
				AssetCreateResultKind.Case.NotHandled => UntypedCreateResult.NotHandled(),
				AssetCreateResultKind.Case.Success => UntypedCreateResult.Success(new DirectPendingAssetValue<T>(
					result.Value ?? throw new AssetLoadException(info.AssetID, typeof(T), "asset creator returned Success but didn't set Value")
				)),
				_ => throw new UnreachableException()
			};
		}
	}

	private sealed class UntypedStagedAssetCreator<T, TPrepared>(IAssetStagedCreator<T, TPrepared> creator)
		: IUntypedAssetCreator where T : class where TPrepared : AssetPreparedData {
		private readonly IAssetStagedCreator<T, TPrepared> creator = creator;
		public Type AssetType => typeof(T);
		public async ValueTask<UntypedCreateResult> TryCreateAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
			AssetPrepareResult<TPrepared> result = await creator.TryPrepareAsync(info, coll, ct).ConfigureAwait(false);
			return result.Kind.Tag switch {
				AssetCreateResultKind.Case.NotHandled => UntypedCreateResult.NotHandled(),
				AssetCreateResultKind.Case.Success => UntypedCreateResult.Success(new StagedPendingAssetValue<T, TPrepared>(creator,
					result.Prepared ?? throw new AssetLoadException(info.AssetID, typeof(T), "asset staged creator returned Success but didn't set Prepared")
				)),
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
	// identification
	private static ulong nextStoreID = 0; // first ID will be 1 since this gets incremented upfront
	private readonly ulong storeID = Interlocked.Increment(ref nextStoreID);

	// ==========================================================================
	// load cycle tracking
	private sealed class AssetLoadStackFrame(AssetKey key, AssetLoadStackFrame? prev) {
		public readonly AssetKey Key = key;
		public readonly AssetLoadStackFrame? Prev = prev;
	}
	private static readonly AsyncLocal<AssetLoadStackFrame?> loadStackTop = new();

	// ==========================================================================
	// general bookkeeping
	private readonly ConcurrentDictionary<AssetKey, IAssetSlot> slots = new();
	private ulong nextSlotID = 0; // first ID will be 1 since this gets incremented upfront

	// ==========================================================================
	// creation pipeline bookkeeping
	private readonly Lock registryLock = new();
	private readonly OwnerOrderedRegistry<IAssetSource> sources = new();
	private readonly OwnerOrderedRegistry<IAssetResolver> resolvers = new();
	private ImmutableDictionary<Type, OwnerOrderedRegistry<IUntypedAssetCreator>> creators = ImmutableDictionary<Type, OwnerOrderedRegistry<IUntypedAssetCreator>>.Empty;

	// ==========================================================================
	// publication / reclamation / thread context bookkeeping
	private readonly ConcurrentQueue<PendingRetired> retired = new();
	private ulong publishedEpoch = 0;
	internal ulong GetPublishedEpoch() => Volatile.Read(ref publishedEpoch);

	[ThreadStatic] private static Dictionary<ulong, AssetThreadContext>? tlsContextsByStoreID;
	private readonly ConcurrentDictionary<ulong, AssetThreadContext> attachedContextsByCtxID = new();

	// ==========================================================================
	// dependency bookkeeping
	private readonly Lock dependencyLock = new();
	private Dictionary<Type, OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>> watchers = new();
	private readonly Dictionary<IAssetDependency, HashSet<IAssetSlot>> slotsByDependency = new();

	// ==========================================================================
	// log/report/failure bookkeeping
	private readonly Lock reloadFailureLock = new();
	private readonly RingBuffer<AssetReloadFailure> reloadFailures = new(MaxBufferedReloadFailures);

	/// <summary>
	/// The maximum number of reload failure records retained by this store's reload-failure buffer.
	/// </summary>
	/// <remarks>
	/// Once the buffer is full, recording another reload failure discards the oldest retained
	/// failure. This buffer is diagnostic state only; per-asset failure state is exposed through
	/// <see cref="AssetRef{T}.LastReloadFailure"/>.
	/// </remarks>
	public const int MaxBufferedReloadFailures = 1024;

	// ==========================================================================
	// public api

	/// <summary>
	/// Gets a stable handle for the specified asset.
	/// </summary>
	/// <typeparam name="T">Expected asset type. Determines what creators are tried.</typeparam>
	/// <param name="id">Asset ID.</param>
	/// <returns>An <see cref="AssetRef{T}"/> handle for the specified asset.</returns>
	public AssetRef<T> GetAsset<T>(AssetID id) where T : class {
		AssetKey key = new(id, typeof(T));
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
		AssetThreadContext ctx = new(this);
		tlsContextsByStoreID ??= new Dictionary<ulong, AssetThreadContext>();
		if (tlsContextsByStoreID.ContainsKey(storeID))
			throw new InvalidOperationException("current thread is already attached to this AssetStore");
		if (!attachedContextsByCtxID.TryAdd(ctx.ID, ctx))
			throw new InternalStateException("duplicate asset thread context id");
		tlsContextsByStoreID.Add(storeID, ctx);
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
		if (tlsContextsByStoreID is null || !tlsContextsByStoreID.TryGetValue(storeID, out AssetThreadContext? ctx))
			throw new InvalidOperationException("the current thread is not attached to this AssetStore. if you're using Task/etc., crossing an await is not guaranteed to resume on the same physical thread, use a real Thread");
		ctx.AtSafeBoundary();
	}

	/// <summary>
	/// Finalizes and applies all currently prepared reloads.
	/// </summary>
	/// <returns>
	/// An <see cref="AssetReloadReport"/> with the number of published reloads and any
	/// finalize failures that occurred.
	/// </returns>
	/// <remarks>
	/// <para>
	/// This method does not start loading or preparing assets. It only publishes replacement versions
	/// that were already prepared by <see cref="AssetRef{T}.QueueReloadAsync(CancellationToken)"/> or
	/// by dependency-triggered reload requests.
	/// </para>
	/// <para>
	/// A failed reload leaves the previous live version active. Finalize failures are both returned
	/// in the report and recorded in the store's reload-failure buffer.
	/// </para>
	/// <para>
	/// Publication is immediate; subsequent borrows after a call observe the new value.
	/// Previously live values are kept alive until every attached thread reports a safe
	/// boundary, at which point they are reclaimed.
	/// </para>
	/// </remarks>
	public AssetReloadReport ApplyQueuedReloads() {
		ImmutableArray<AssetReloadFailure>.Builder failures = ImmutableArray.CreateBuilder<AssetReloadFailure>();
		ulong epoch = Interlocked.Increment(ref publishedEpoch);
		int n = 0;
		foreach (IAssetSlot s in slots.Values.ToArray())
			if (s.ApplyQueuedReload(epoch, failures))
				n++;
		TryCollectRetired();
		return new AssetReloadReport(n, failures.ToImmutable());
	}

	/// <summary>
	/// Finalizes and applies all queued asset reloads, throwing if any of them failed.
	/// </summary>
	/// <returns>The number of assets that published a new live version.</returns>
	/// <remarks>
	/// Convenience wrapper over <see cref="ApplyQueuedReloads()"/>.
	/// Has equivalent throwing behavior to <see cref="AssetReloadReport.ThrowIfFailed()"/>.
	/// </remarks>
	/// <exception cref="AggregateException">
	/// Thrown if finalization of one or more prepared reloads failed.
	/// </exception>
	public int ApplyQueuedReloadsOrThrow() {
		AssetReloadReport r = ApplyQueuedReloads();
		r.ThrowIfFailed();
		return r.AppliedCount;
	}


	/// <summary>
	/// Drains this store's buffered reload failures.
	/// </summary>
	/// <returns>
	/// The reload failures that had been recorded by this store in oldest-to-newest order.
	/// </returns>
	/// <remarks>
	/// <para>
	/// Draining clears the store-level reload failure buffer, intended for diagnostics such as
	/// logging / editor UIs / debug overlays / etc. It is not needed to determine the current
	/// state of a specific asset; use <see cref="AssetRef{T}.LastReloadFailure"/> for that.
	/// </para>
	/// <para>
	/// Dependency-triggered reload failures are recorded here because they do not throw into the
	/// dependency watcher event path. Finalize failures from <see cref="ApplyQueuedReloads()"/>
	/// are also recorded here.
	/// </para>
	/// </remarks>
	public AssetReloadFailure[] DrainReloadFailures() {
		lock (reloadFailureLock) {
			AssetReloadFailure[] ret = reloadFailures.ToArray();
			reloadFailures.Clear();
			return ret;
		}
	}

	/// <summary>
	/// Registers an asset source.
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
	public AssetSourceHandle RegisterSource(string ownerID, IAssetSource source, string localID,
		int localPriority = 0, IEnumerable<string>? beforeOwners = null, IEnumerable<string>? afterOwners = null) {
		ArgumentNullException.ThrowIfNull(source);
		lock (registryLock) {
			ulong id = sources.Register(new OwnerOrderedEntry<IAssetSource>(
				source,
				ownerID, localID, localPriority, beforeOwners, afterOwners
			));
			return new AssetSourceHandle(storeID, id);
		}
	}

	/// <summary>
	/// Registers an asset resolver.
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
			ulong id = resolvers.Register(new OwnerOrderedEntry<IAssetResolver>(
				resolver,
				ownerID, localID, localPriority, beforeOwners, afterOwners
			));
			return new AssetResolverHandle(storeID, id);
		}
	}

	private AssetCreatorHandle registerCreatorLocked(string ownerID, IUntypedAssetCreator creator, Type type, string localID,
		int localPriority = 0, IEnumerable<string>? beforeOwners = null, IEnumerable<string>? afterOwners = null) {
		OwnerOrderedEntry<IUntypedAssetCreator> ent = new(
			creator,
			ownerID, localID, localPriority, beforeOwners, afterOwners
		);
		ImmutableDictionary<Type, OwnerOrderedRegistry<IUntypedAssetCreator>> old = creators;
		ulong id;
		if (old.TryGetValue(type, out OwnerOrderedRegistry<IUntypedAssetCreator>? reg)) {
			id = reg.Register(ent);
		} else {
			reg = new OwnerOrderedRegistry<IUntypedAssetCreator>();
			id = reg.Register(ent);
			Volatile.Write(ref creators, old.Add(type, reg));
		}
		return new AssetCreatorHandle(storeID, type, id);
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
	/// <paramref name="localID"/> must be unique among all other creators for the type <typeparamref name="T"/>
	/// registered in this <see cref="AssetStore"/> instance under this <paramref name="ownerID"/>.
	/// </remarks>
	/// <exception cref="OwnerOrderingException">
	/// Thrown if the new ordering constraints are invalid or unsatisfiable.
	/// </exception>
	public AssetCreatorHandle RegisterCreator<T>(string ownerID, IAssetCreator<T> creator, string localID,
		int localPriority = 0, IEnumerable<string>? beforeOwners = null, IEnumerable<string>? afterOwners = null)
		where T : class {
		ArgumentNullException.ThrowIfNull(creator);
		lock (registryLock) {
			return registerCreatorLocked(ownerID, new UntypedDirectAssetCreator<T>(creator), typeof(T), localID,
				localPriority, beforeOwners, afterOwners);
		}
	}

	/// <summary>
	/// Registers a staged asset creator of a specific asset type.
	/// </summary>
	/// <typeparam name="T">Asset type produced by the creator.</typeparam>
	/// <typeparam name="TPrepared">Prepared-data intermediate type used by the creator.</typeparam>
	/// <inheritdoc cref="RegisterCreator{T}(string, IAssetCreator{T}, string, int, IEnumerable{string}?, IEnumerable{string}?)"/>
	public AssetCreatorHandle RegisterStagedCreator<T, TPrepared>(string ownerID, IAssetStagedCreator<T, TPrepared> creator, string localID,
		int localPriority = 0, IEnumerable<string>? beforeOwners = null, IEnumerable<string>? afterOwners = null)
		where T : class where TPrepared : AssetPreparedData {
		ArgumentNullException.ThrowIfNull(creator);
		lock (registryLock) {
			return registerCreatorLocked(ownerID, new UntypedStagedAssetCreator<T, TPrepared>(creator), typeof(T), localID,
				localPriority, beforeOwners, afterOwners);
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
		UntypedAssetDependencyWatcher<T> untyped = new(watcher);
		lock (dependencyLock) {
			OwnerOrderedEntry<IUntypedAssetDependencyWatcher> ent = new(
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
				Dictionary<Type, OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>> @new = new(old) {
					[typeof(T)] = reg
				};
				Volatile.Write(ref watchers, @new);
			}
			untyped.Changed += onDependencyChanged;
			foreach ((IAssetDependency dep, _) in slotsByDependency)
				if (dep.GetType() == typeof(T))
					watcher.Watch((T)dep);
			return new AssetDependencyWatcherHandle(storeID, typeof(T), id);
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
	/// Thrown if the handle doesn't point to a registered source (typically
	/// because it has already been unregistered) or if it was obtained from
	/// another <see cref="AssetStore"/>.
	/// </exception>
	public void UnregisterSource(AssetSourceHandle handle) {
		if (handle.ID == 0)
			throw new ArgumentException("invalid handle (did you accidentally pass `default`?)", nameof(handle));
		if (handle.StoreID != storeID)
			throw new InvalidOperationException("this handle belongs to another asset store");
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
	/// Thrown if the handle doesn't point to a registered resolver (typically
	/// because it has already been unregistered) or if it was obtained from
	/// another <see cref="AssetStore"/>.
	/// </exception>
	public void UnregisterResolver(AssetResolverHandle handle) {
		if (handle.ID == 0)
			throw new ArgumentException("invalid handle (did you accidentally pass `default`?)", nameof(handle));
		if (handle.StoreID != storeID)
			throw new InvalidOperationException("this handle belongs to another asset store");
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
		if (handle.StoreID != storeID)
			throw new InvalidOperationException("this handle belongs to another asset store");
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
		if (handle.DependencyType is null || handle.ID == 0)
			throw new ArgumentException("invalid handle (did you accidentally pass `default`?)", nameof(handle));
		if (handle.StoreID != storeID)
			throw new InvalidOperationException("this handle belongs to another asset store");
		lock (dependencyLock) {
			if (!watchers.TryGetValue(handle.DependencyType, out OwnerOrderedRegistry<IUntypedAssetDependencyWatcher>? reg))
				throw new InternalStateException("this handle's AssetType doesn't have a corresponding OwnerOrderedRegistry of watchers, how did it even get handed out?");
			if (!reg.Unregister(handle.ID, out OwnerOrderedEntry<IUntypedAssetDependencyWatcher>? ent))
				throw new InvalidOperationException("this handle doesn't point to a registered watcher");
			ent.Item.Dispose();
		}
	}

	// ==========================================================================
	// internal api
	internal void DetachThread(AssetThreadContext ctx) {
		attachedContextsByCtxID.TryRemove(ctx.ID, out _);
		if (tlsContextsByStoreID is null || !tlsContextsByStoreID.Remove(storeID))
			throw new InvalidOperationException("tried to detach a thread that is not attached to this AssetStore. if you're using Task/etc., crossing an await is not guaranteed to resume on the same physical thread, use a real Thread");
	}

	internal void RecordFailedReload(AssetReloadFailure f) {
		lock (reloadFailureLock)
			reloadFailures.PushNewest(f);
	}

	internal void QueueRetire<T>(T val, ulong epoch) where T : class {
		retired.Enqueue(new PendingRetired(val, epoch));
	}

	internal void TryCollectRetired() {
		ulong cutoff = ulong.MaxValue;
		foreach (AssetThreadContext ctx in attachedContextsByCtxID.Values) {
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

	private async ValueTask<(IPendingAssetValue Prepared, ImmutableArray<IAssetDependency> Dependencies)>
		tryPrepareValueAsync<T>(AssetID id, CancellationToken ct = default) where T : class {
		AssetKey key = new(id, typeof(T));
		AssetLoadStackFrame? prev = loadStackTop.Value;
		for (AssetLoadStackFrame? f = prev; f is not null; f = f.Prev)
			if (f.Key == key)
				throw makeCycleException(key, prev!);
		loadStackTop.Value = new AssetLoadStackFrame(key, prev);

		try {
			DependencyCollector rootColl = new();
			using AssetData data = await tryAllResolversAsync(id, rootColl, typeof(T), ct).ConfigureAwait(false);

			AssetCreateInfo info = new(id, data);
			ImmutableDictionary<Type, OwnerOrderedRegistry<IUntypedAssetCreator>> dictsnap = Volatile.Read(ref creators);
			if (dictsnap.TryGetValue(typeof(T), out OwnerOrderedRegistry<IUntypedAssetCreator>? reg)) {
				IReadOnlyList<IUntypedAssetCreator> snapshot = reg.ReadSnapshot();

				foreach (IUntypedAssetCreator creator in snapshot) {
					DependencyCollector childColl = new();
					UntypedCreateResult res = await creator.TryCreateAsync(info, childColl, ct).ConfigureAwait(false);
					switch (res.Kind.Tag) {
					case AssetCreateResultKind.Case.NotHandled:
						continue;
					case AssetCreateResultKind.Case.Success:
						if (res.Value is null)
							throw new InternalStateException("untyped asset creator prepare returned Success but didn't set Value");
						rootColl.MergeFrom(childColl);
						return (res.Value, rootColl.Snapshot());
					default:
						throw new UnreachableException();
					}
				}
			}
			throw new AssetUnhandledException(id, typeof(T), "no registered asset creator accepted the asset");
		} finally {
			loadStackTop.Value = prev;
		}
	}

	private static T finalizePrepared<T>(AssetID id, IPendingAssetValue pv) where T : class {
		object v = pv.FinalizeValue(id);
		if (v is not T typed)
			throw new InternalStateException($"pending asset value returned some value of type {v.GetType().FullName}, expected the {typeof(T).FullName} from the creator");
		return typed;
	}

	// ==========================================================================
	// the rest of the asset pipeline
	private static async ValueTask<Stream> fixstream(Stream stream, CancellationToken ct) {
		if (stream.CanSeek)
			return stream;
		MemoryStream ms = new();
		try {
			await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
			ms.Position = 0;
			return ms;
		} catch {
			ms.Dispose();
			throw;
		} finally {
			stream.Dispose();
		}
	}

	private async ValueTask<Stream?> tryAllSourcesAsyncOrNull(AssetID id, DependencyCollector parentColl, Type t, CancellationToken ct) {
		IReadOnlyList<IAssetSource> snapshot = sources.ReadSnapshot();
		AssetSourceInfo info = new(id);
		foreach (IAssetSource source in snapshot) {
			DependencyCollector childColl = new();
			AssetSourceResult res = await source.TrySourceAsync(info, childColl, ct).ConfigureAwait(false);
			switch (res.Kind.Tag) {
			case AssetSourceResultKind.Case.NotHandled:
				continue;
			case AssetSourceResultKind.Case.Success:
				if (res.Stream is null)
					throw new AssetLoadException(id, t, "asset source returned Success but didn't set Stream");
				parentColl.MergeFrom(childColl);
				return await fixstream(res.Stream, ct).ConfigureAwait(false);
			default:
				throw new UnreachableException();
			}
		}
		return null;
	}

	private async ValueTask<Stream> tryAllSourcesAsync(AssetID id, DependencyCollector parentColl, Type t, CancellationToken ct) =>
		await tryAllSourcesAsyncOrNull(id, parentColl, t, ct) ?? throw new AssetUnhandledException(id, t, "no registered asset source managed to provide the asset");

	private async ValueTask<AssetData> tryAllResolversAsync(AssetID id, DependencyCollector parentColl, Type t, CancellationToken ct) {
		IReadOnlyList<IAssetResolver> snapshot = resolvers.ReadSnapshot();
		foreach (IAssetResolver resolver in snapshot) {
			DependencyCollector childColl = new();
			AssetResolveInfo info = new(id,
				(AssetID toFetch, CancellationToken passedCt) => tryAllSourcesAsync(toFetch, childColl, t, passedCt),
				(AssetID toFetch, CancellationToken passedCt) => tryAllSourcesAsyncOrNull(toFetch, childColl, t, passedCt)
			);
			AssetResolveResult res = await resolver.TryResolveAsync(info, childColl, ct).ConfigureAwait(false);
			switch (res.Kind.Tag) {
			case AssetResolveResultKind.Case.NotHandled:
				continue;
			case AssetResolveResultKind.Case.Success:
				if (res.Data is null)
					throw new AssetLoadException(id, t, "asset resolver returned Success but didn't set Data");
				parentColl.MergeFrom(childColl);
				return res.Data;
			default:
				throw new UnreachableException();
			}
		}
		throw new AssetUnhandledException(id, t, "no registered asset resolver managed to resolve the asset");
	}

	// ==========================================================================
	// dependencies
	private void onDependencyChanged(IAssetDependency dependency) {
		IAssetSlot[] affected;
		lock (dependencyLock) {
			if (!slotsByDependency.TryGetValue(dependency, out HashSet<IAssetSlot>? slots))
				return;
			affected = slots.ToArray();
		}
		foreach (IAssetSlot slot in affected)
			slot.QueueReloadFromDependency(dependency);
	}

	private void onSlotPublished(IAssetSlot slot, ImmutableArray<IAssetDependency> oldDeps, ImmutableArray<IAssetDependency> newDeps) {
		static HashSet<IAssetDependency> makeSet(ImmutableArray<IAssetDependency> deps) {
			HashSet<IAssetDependency> s = new();
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
	private static string formatCycle(ImmutableArray<AssetKey> cycle) {
		StringBuilder sb = new("recursive asset load detected: ");
		for (int i = 0; i < cycle.Length; i++) {
			if (i > 0)
				sb.Append(" -> ");
			sb.Append(cycle[i].AssetType.Name).Append('(').Append(cycle[i].AssetID).Append(')');
		}
		return sb.ToString();
	}

	private static AssetLoadCycleException makeCycleException(AssetKey repeated, AssetLoadStackFrame prev) {
		ImmutableArray<AssetKey>.Builder builder = ImmutableArray.CreateBuilder<AssetKey>();
		for (AssetLoadStackFrame? f = prev; f is not null; f = f.Prev) {
			builder.Add(f.Key);
			if (f.Key == repeated)
				break;
		}
		builder.Reverse();
		builder.Add(repeated);
		ImmutableArray<AssetKey> cycle = builder.ToImmutable();
		return new AssetLoadCycleException(repeated.AssetID, repeated.AssetType, formatCycle(cycle), cycle);
	}
}
