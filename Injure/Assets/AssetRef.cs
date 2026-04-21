// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Injure.Assets;

/// <summary>
/// Untyped wrapper over an <see cref="AssetRef{T}"/> used by bulk operations and
/// generic tooling.
/// </summary>
public interface IUntypedAssetRef {
	/// <summary>Asset type.</summary>
	Type AssetType { get; }

	/// <summary>The asset ID.</summary>
	AssetID AssetID { get; }

	/// <summary>Whether a live version currently exists.</summary>
	bool IsLoaded { get; }

	/// <summary>Whether a prepared replacement version is waiting to be applied.</summary>
	bool HasQueuedReload { get; }

	/// <summary>Last exception that occurred while initially loading this asset, if any.</summary>
	Exception? LastLoadException { get; }

	/// <summary>Last reload failure recorded for this asset, if any.</summary>
	AssetReloadFailure? LastReloadFailure { get; }

	/// <summary>Ensures that a current live version exists.</summary>
	Task WarmAsync(CancellationToken ct = default);

	/// <summary>Prepares a replacement version to later be applied by <see cref="AssetStore.ApplyQueuedReloads()"/>.</summary>
	Task QueueReloadAsync(CancellationToken ct = default);

	/// <summary>Synchronous blocking wrapper over <see cref="WarmAsync(CancellationToken)"/>.</summary>
	void Warm(CancellationToken ct = default);

	/// <summary>Synchronous blocking wrapper over <see cref="QueueReloadAsync(CancellationToken)"/>.</summary>
	void QueueReload(CancellationToken ct = default);
}

/// <summary>
/// Stable handle for an asset.
/// </summary>
/// <typeparam name="T">Asset type.</typeparam>
/// <remarks>
/// An <see cref="AssetRef{T}"/> is cheap to keep and copy. It is a logical representation
/// of an asset (be it a file in an "assets" directory, a database entry, an HTTP resource...)
/// that stays steady even if the underlying value changes, since the identity is the same.
/// Use <see cref="Borrow()"/> or <see cref="TryPassiveBorrow(out AssetLease{T})"/> to access
/// the current live version.
/// </remarks>
public sealed class AssetRef<T> : IUntypedAssetRef where T : class {
	internal readonly AssetStore.AssetSlot<T> Slot;
	internal ulong SlotID => Slot.SlotID;
	internal AssetRef(AssetStore.AssetSlot<T> slot) {
		Slot = slot;
	}

	/// <summary>
	/// Asset type; provided for <see cref="IUntypedAssetRef"/> convenience.
	/// </summary>
	public Type AssetType => Slot.AssetType; // should be equivalent to typeof(T)

	/// <summary>
	/// The asset ID.
	/// </summary>
	public AssetID AssetID => Slot.AssetID;

	/// <summary>
	/// Whether a live version currently exists.
	/// </summary>
	public bool IsLoaded => Slot.HasCurrent;

	/// <summary>
	/// Whether a prepared replacement version is waiting to be applied.
	/// </summary>
	public bool HasQueuedReload => Slot.HasQueuedReload;

	/// <summary>
	/// Last exception that occurred while initially loading this asset, if any.
	/// </summary>
	/// <remarks>
	/// Reload failures are reported separately through <see cref="LastReloadFailure"/>.
	/// </remarks>
	public Exception? LastLoadException => Slot.LastLoadException;

	/// <summary>
	/// Last reload failure recorded for this asset, if any.
	/// </summary>
	/// <remarks>
	/// Cleared by a successful reload.
	/// </remarks>
	public AssetReloadFailure? LastReloadFailure => Slot.LastReloadFailure;

	/// <summary>
	/// Actively borrows the current live version.
	/// </summary>
	/// <returns>
	/// An <see cref="AssetLease{T}"/> over the current live version.
	/// </returns>
	/// <remarks>
	/// If the asset is unloaded (doesn't have a live version), this method may block and
	/// synchronously materialize the first live version.
	/// </remarks>
	/// <exception cref="AssetLoadException">
	/// Thrown if the asset has no live version and initial materialization fails.
	/// </exception>
	/// <exception cref="AssetUnhandledException">
	/// Thrown if the pipeline did not provide the requested asset.
	/// </exception>
	public AssetLease<T> Borrow() => Slot.Borrow();

	/// <summary>
	/// Passively borrows the current live version.
	/// </summary>
	/// <param name="lease">An <see cref="AssetLease{T}"/> for the current live version, if one exists.</param>
	/// <returns>
	/// <see langword="true"/> if a current live version exists; otherwise <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// <para>This method never blocks or creates any new versions of the asset, including initial materialization.</para>
	/// <para>Guaranteed to succeed after <see cref="WarmAsync(CancellationToken)"/> or its blocking wrapper.</para>
	/// </remarks>
	public bool TryPassiveBorrow(out AssetLease<T> lease) => Slot.TryPassiveBorrow(out lease);

	/// <summary>
	/// Ensures that a current live version exists.
	/// </summary>
	/// <remarks>
	/// <para>Concurrent calls share the same initial materialization work.</para>
	/// <para>After successful completion, <see cref="TryPassiveBorrow(out AssetLease{T})"/> is guaranteed to succeed.</para>
	/// </remarks>
	/// <exception cref="AssetLoadException">
	/// Thrown if the asset has no live version and initial materialization fails.
	/// </exception>
	/// <exception cref="AssetUnhandledException">
	/// Thrown if the pipeline did not provide the requested asset.
	/// </exception>
	public Task WarmAsync(CancellationToken ct = default) => Slot.WarmAsync(ct);

	/// <summary>
	/// Prepares a replacement version to later be applied by <see cref="AssetStore.ApplyQueuedReloads()"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Concurrent reload requests are coalesced; the published version number reflects accepted
	/// reload request count, but fewer prepares of the asset may happen.
	/// </para>
	/// <para>
	/// If the asset is currently unloaded, behaves like <see cref="WarmAsync(CancellationToken)"/>.
	/// </para>
	/// </remarks>
	/// <exception cref="AssetLoadException">
	/// Thrown if the asset has no live version and initial materialization fails.
	/// </exception>
	/// <exception cref="AssetUnhandledException">
	/// Thrown if the pipeline did not provide the requested asset.
	/// </exception>
	public Task QueueReloadAsync(CancellationToken ct = default) => Slot.QueueReloadAsync(ct);

	/// <summary>
	/// Synchronous blocking wrapper over <see cref="WarmAsync(CancellationToken)"/>.
	/// </summary>
	/// <inheritdoc cref="WarmAsync(CancellationToken)"/>
	public void Warm(CancellationToken ct = default) => WarmAsync(ct).GetAwaiter().GetResult();

	/// <summary>
	/// Synchronous blocking wrapper over <see cref="QueueReloadAsync(CancellationToken)"/>.
	/// </summary>
	/// <inheritdoc cref="QueueReloadAsync(CancellationToken)"/>
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
