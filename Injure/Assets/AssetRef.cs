// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Injure.Assets;

/// <summary>
/// Untyped wrapper over an <see cref="AssetRef{T}"/> used by bulk warm / reload helpers.
/// </summary>
public interface IUntypedAssetRef {
	AssetID AssetID { get; }
	bool IsLoaded { get; }
	bool HasQueuedReload { get; }
	Exception? LastException { get; }

	Task WarmAsync(CancellationToken ct = default);
	Task QueueReloadAsync(CancellationToken ct = default);
}

/// <summary>
/// Stable handle for an asset.
/// </summary>
/// <typeparam name="T">Asset type.</typeparam>
public sealed class AssetRef<T> : IUntypedAssetRef where T : class {
	internal readonly AssetStore.AssetSlot<T> Slot;
	internal ulong SlotID => Slot.SlotID;
	internal AssetRef(AssetStore.AssetSlot<T> slot) {
		Slot = slot;
	}

	/// <summary>
	/// Gets the asset ID.
	/// </summary>
	public AssetID AssetID => Slot.AssetID;

	/// <summary>
	/// Gets whether a live version currently exists.
	/// </summary>
	public bool IsLoaded => Slot.HasCurrent;

	/// <summary>
	/// Gets whether a replacement version has been prepared and is in queue to be applied.
	/// </summary>
	public bool HasQueuedReload => Slot.HasQueuedReload;

	/// <summary>
	/// Gets the last exception that occurred during load, warm, or reload.
	/// </summary>
	public Exception? LastException => Slot.LastException;

	/// <summary>
	/// Actively borrows the current live version.
	/// </summary>
	/// <returns>
	/// An <see cref="AssetLease{T}"/> for the current live version.
	/// </returns>
	/// <remarks>
	/// <para>
	/// If the asset is unloaded (doesn't have a live version), this method may
	/// do a blocking materialize of the first live version; if that materialize
	/// operation sets <see cref="LastException"/>, this method will also throw it.
	/// </para>
	/// <para>
	/// This method never applies a queued reload over an existing live version.
	/// </para>
	/// </remarks>
	public AssetLease<T> Borrow() => Slot.Borrow();

	/// <summary>
	/// Passively borrows the current live version.
	/// </summary>
	/// <param name="lease">An <see cref="AssetLease{T}"/> for the current live version, if one exists.</param>
	/// <returns>
	/// <see langword="true"/> if a current live version exists; otherwise <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// This method never blocks, materializes, or applies queued reloads.
	/// </remarks>
	public bool TryPassiveBorrow(out AssetLease<T> lease) => Slot.TryPassiveBorrow(out lease);

	/// <summary>
	/// Ensures that a current live version exists.
	/// </summary>
	/// <param name="ct">Cancellation token.</param>
	/// <remarks>
	/// After successful completion, <see cref="TryPassiveBorrow(out AssetLease{T})"/> is guaranteed to succeed.
	/// </remarks>
	public Task WarmAsync(CancellationToken ct = default) => Slot.WarmAsync(ct);

	/// <summary>
	/// Prepares a replacement version to later be applied by <see cref="AssetStore.ApplyQueuedReloads()"/>.
	/// </summary>
	/// <param name="ct">Cancellation token.</param>
	/// <remarks>
	/// If the asset is currently unloaded, behaves like <see cref="WarmAsync(CancellationToken)"/>.
	/// </remarks>
	public Task QueueReloadAsync(CancellationToken ct = default) => Slot.QueueReloadAsync(ct);

	/// <summary>
	/// Synchronous blocking wrapper over <see cref="WarmAsync(CancellationToken)"/>.
	/// </summary>
	/// <param name="ct">Cancellation token.</param>
	/// <remarks>
	/// After successful completion, <see cref="TryPassiveBorrow(out AssetLease{T})"/> is guaranteed to succeed.
	/// </remarks>
	public void Warm(CancellationToken ct = default) => WarmAsync(ct).GetAwaiter().GetResult();

	/// <summary>
	/// Synchronous blocking wrapper over <see cref="QueueReloadAsync(CancellationToken)"/>.
	/// </summary>
	/// <param name="ct">Cancellation token.</param>
	/// <remarks>
	/// If the asset is currently unloaded, behaves like <see cref="Warm(CancellationToken)"/>.
	/// </remarks>
	public void QueueReload(CancellationToken ct = default) => QueueReloadAsync(ct).GetAwaiter().GetResult();
}

/// <summary>
/// View of a borrowed live asset value.
/// </summary>
/// <remarks>
/// The lease object itself is a <c>ref struct</c> to make misuse harder, but
/// making sure the <see cref="Value"/> doesn't get cached beyond the intended
/// borrow scope is still the caller's responsibility. If <see cref="Value"/>
/// implements <see cref="IRevokable"/>, it will be revoked on reclamation.
/// </remarks>
public readonly ref struct AssetLease<T> where T : class {
	/// <summary>
	/// The live asset value.
	/// </summary>
	public T Value { get; }

	/// <summary>
	/// Monotonically incremented with each new version of the asset. The
	/// starting value is 1.
	/// </summary>
	public ulong Version { get; }

	/// <summary>
	/// The dependency list for the published version that this lease is a view of.
	/// </summary>
	public ReadOnlySpan<IAssetDependency> Dependencies { get; }

	internal AssetLease(T value, ulong ver, ReadOnlySpan<IAssetDependency> deps) {
		Value = value;
		Version = ver;
		Dependencies = deps;
	}
}

/// <summary>
/// Helpers for concurrent bulk operations on <see cref="AssetRef{T}"/>s.
/// </summary>
public static class AssetRefExtensions {
	/// <summary>
	/// Ensures that a current live version exists for all of the assets.
	/// </summary>
	/// <param name="assetRefs">Assets to warm.</param>
	/// <param name="maxConcurrency">Maximum number of concurrent warm operations.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <remarks>
	/// After successful completion, <see cref="AssetRef{T}.TryPassiveBorrow(out AssetLease{T})"/>
	/// is guaranteed to succeed for all of the assets.
	/// </remarks>
	public static async Task WarmAllAsync(this IEnumerable<IUntypedAssetRef> assetRefs, int maxConcurrency = 8, CancellationToken ct = default) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
		using SemaphoreSlim sem = new SemaphoreSlim(maxConcurrency, maxConcurrency);
		await Task.WhenAll(assetRefs.Select(async (IUntypedAssetRef assetRef) => {
			await sem.WaitAsync(ct).ConfigureAwait(false);
			try {
				await assetRef.WarmAsync(ct).ConfigureAwait(false);
			} finally {
				sem.Release();
			}
		})).ConfigureAwait(false);
	}

	/// <summary>
	/// Prepares a replacement version to later be applied by <see cref="AssetStore.ApplyQueuedReloads()"/>
	/// for all of the assets.
	/// </summary>
	/// <param name="assetRefs">Assets to queue a reload for.</param>
	/// <param name="maxConcurrency">Maximum number of concurrent queue-reload operations.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <remarks>
	/// If an asset is currently unloaded, behaves like <see cref="AssetRef{T}.WarmAsync(CancellationToken)"/>
	/// for that asset.
	/// </remarks>
	public static async Task QueueReloadAllAsync(this IEnumerable<IUntypedAssetRef> assetRefs, int maxConcurrency = 8, CancellationToken ct = default) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
		using SemaphoreSlim sem = new SemaphoreSlim(maxConcurrency, maxConcurrency);
		await Task.WhenAll(assetRefs.Select(async (IUntypedAssetRef assetRef) => {
			await sem.WaitAsync(ct).ConfigureAwait(false);
			try {
				await assetRef.QueueReloadAsync(ct).ConfigureAwait(false);
			} finally {
				sem.Release();
			}
		})).ConfigureAwait(false);
	}

	/// <summary>
	/// Synchronous blocking wrapper over <see cref="WarmAllAsync(IEnumerable{IUntypedAssetRef}, int, CancellationToken)"/>.
	/// </summary>
	/// <param name="assetRefs">Assets to warm.</param>
	/// <param name="maxConcurrency">Maximum number of concurrent warm operations.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <remarks>
	/// After successful completion, <see cref="AssetRef{T}.TryPassiveBorrow(out AssetLease{T})"/>
	/// is guaranteed to succeed for all of the assets.
	/// </remarks>
	public static void WarmAll(this IEnumerable<IUntypedAssetRef> assetRefs, int maxConcurrency = 8, CancellationToken ct = default) =>
		assetRefs.WarmAllAsync(maxConcurrency, ct).GetAwaiter().GetResult();

	/// <summary>
	/// Synchronous blocking wrapper over <see cref="QueueReloadAllAsync(IEnumerable{IUntypedAssetRef}, int, CancellationToken)"/>.
	/// </summary>
	/// <param name="assetRefs">Assets to queue a reload for.</param>
	/// <param name="maxConcurrency">Maximum number of concurrent queue-reload operations.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <remarks>
	/// If an asset is currently unloaded, behaves like <see cref="AssetRef{T}.WarmAsync(CancellationToken)"/>
	/// for that asset.
	/// </remarks>
	public static void QueueReloadAll(this IEnumerable<IUntypedAssetRef> assetRefs, int maxConcurrency = 8, CancellationToken ct = default) =>
		assetRefs.QueueReloadAllAsync(maxConcurrency, ct).GetAwaiter().GetResult();
}
