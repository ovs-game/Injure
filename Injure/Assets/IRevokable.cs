// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// Allows an asset-backed object to be explicitly invalidated.
/// </summary>
/// <remarks>
/// <para>
/// Revocation is logical invalidation only; the object may still need to be disposed
/// to release held resources.
/// </para>
/// <para>
/// Since use after revocation is a logic bug, implementors are expected to fail fast
/// on any attempt to use the object after <see cref="Revoke()"/>, typically by throwing
/// <see cref="AssetLeaseExpiredException"/>.
/// </para>
/// </remarks>
public interface IRevokable {
	void Revoke();
}
