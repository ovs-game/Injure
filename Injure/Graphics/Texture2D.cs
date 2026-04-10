// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Silk.NET.WebGPU;

using Injure.Assets;
using Injure.Rendering;

namespace Injure.Graphics;

/// <summary>
/// High-level wrapper for a 2D color texture.
/// </summary>
/// <remarks>
/// <para>
/// Owns a color texture, color sampler, and a lazy-created bind group for the texture's
/// default view + the sampler.
/// </para>
/// <para>
/// If this <see cref="Texture2D"/> was obtained by borrowing an <see cref="AssetRef{Texture2D}"/>,
/// it will be revoked once that lease expires to make misuse more difficult. After revocation,
/// further usage attempts throw <see cref="AssetLeaseExpiredException"/>.
/// </para>
/// </remarks>
public sealed class Texture2D(WebGPUDevice device, GPUTexture texture, GPUSampler sampler) : IRevokable, IDisposable {
	private readonly WebGPUDevice device = device ?? throw new ArgumentNullException(nameof(device));
	private readonly GPUTexture texture = texture ?? throw new ArgumentNullException(nameof(texture));
	private readonly GPUSampler sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
	private GPUBindGroup? bindGroup = null;
	private int disposed = 0;
	private int revoked = 0;

	internal GPUTexture Texture { get { chk(); return texture; } }
	internal GPUSampler Sampler { get { chk(); return sampler; } }
	internal GPUBindGroupRef BindGroup { get { chk(); return (bindGroup ??= device.CreateStdColorTexture2DBindGroup(Texture, Sampler)).AsRef(); } }

	/// <summary>
	/// Returns the underlying <see cref="GPUTexture"/>, bypassing ownership/lifetime/revocation contracts.
	/// </summary>
	public GPUTexture DangerousGetGPUTexture() => Texture;

	/// <summary>
	/// Returns the underlying <see cref="GPUSampler"/>, bypassing ownership/lifetime/revocation contracts.
	/// </summary>
	public GPUSampler DangerousGetGPUSampler() => Sampler;

	/// <summary>
	/// Returns a ref wrapper to the underlying <see cref="GPUBindGroup"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public GPUBindGroupRef DangerousGetBindGroup() => BindGroup;

	// these three below should be fine as long as Texture calls chk()

	/// <summary>
	/// Width of the texture in texels.
	/// </summary>
	public uint Width => Texture.Width;

	/// <summary>
	/// Height of the texture in texels.
	/// </summary>
	public uint Height => Texture.Height;

	/// <summary>
	/// Texture format. Is a color format.
	/// </summary>
	public TextureFormat Format => Texture.Format;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void chk() {
		// check revoked first for more helpful use-after-lease-expiry exceptions
		if (Volatile.Read(ref revoked) != 0)
			throw new AssetLeaseExpiredException("borrowed texture was used after its lease expired");
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
	}

	/// <summary>
	/// Revokes this texture, invalidating further use.
	/// </summary>
	/// <remarks>
	/// This is used by the asset system when a leased <see cref="Texture2D"/> obtained by
	/// borrowing a <see cref="AssetRef{Texture2D}"/> expires. Revocation is logical
	/// invalidation only; the underlying GPU resources remain alive until <see cref="Dispose"/>.
	/// </remarks>
	public void Revoke() {
		if (Volatile.Read(ref disposed) != 0)
			return;
		Volatile.Write(ref revoked, 1);
	}

	/// <summary>
	/// Releases the owned GPU resources.
	/// </summary>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;

		bindGroup?.Dispose();
		sampler.Dispose();
		texture.Dispose();
	}
}
