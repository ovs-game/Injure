// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Injure.Assets;

/// <summary>
/// Per-thread attachment to an <see cref="AssetStore"/> for deferred asset
/// reclamation tracking.
/// </summary>
public sealed class AssetThreadContext : IDisposable {
	private static ulong nextID = 0;
	private readonly AssetStore owner;
	private int disposed = 0;

	internal ulong ID { get; }
	internal ulong QuiescentEpoch; // owner writes to here

	internal AssetThreadContext(AssetStore owner) {
		this.owner = owner;
		ID = Interlocked.Increment(ref nextID);
	}

	/// <summary>
	/// Reports a safe boundary.
	/// </summary>
	/// <remarks>
	/// By calling this, the current thread is declaring that it is okay with any
	/// asset leases borrowed before this call being reclaimed and invalidated.
	/// </remarks>
	/// <see cref="AssetStore"/>.
	public void AtSafeBoundary() {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		Volatile.Write(ref QuiescentEpoch, owner.GetPublishedEpoch());
		owner.TryCollectRetired();
	}

	/// <summary>
	/// Detaches this thread context from its <see cref="AssetStore"/>.
	/// </summary>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		Volatile.Write(ref QuiescentEpoch, owner.GetPublishedEpoch());
		owner.TryCollectRetired();
		owner.DetachThread(this);
	}
}
