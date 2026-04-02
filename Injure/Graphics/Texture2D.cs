// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using Silk.NET.WebGPU;

using Injure.Assets;
using Injure.Rendering;

namespace Injure.Graphics;

/// <summary>
/// High-level wrapper for a 2D texture, backed by a <see cref="GPUTexture"/> and <see cref="GPUSampler"/>.
/// </summary>
/// <remarks>
/// <see cref="Texture2D"/> is the standard texture type used by high-level drawing APIs.
/// It owns the underlying <see cref="GPUTexture"/> and <see cref="GPUSampler"/>, and lazily
/// creates a bind group for the tex + sampler on-demand.
///
/// If this <see cref="Texture2D"/> was obtained by borrowing an <see cref="AssetRef{Texture2D}"/>,
/// it will be revoked once that lease expires to make misuse more difficult. After revocation,
/// further usage attempts throw <see cref="AssetLeaseExpiredException"/>.
/// </remarks>
public sealed class Texture2D(WebGPURenderer renderer, GPUTexture texture, GPUSampler sampler) : IRevokable, IDisposable {
	private readonly WebGPURenderer renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
	private readonly GPUTexture texture = texture ?? throw new ArgumentNullException(nameof(texture));
	private readonly GPUSampler sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
	private GPUBindGroup? bindGroup = null;
	private int disposed = 0;
	private int revoked = 0;

	internal GPUTexture Texture { get => alive(texture); }
	internal GPUSampler Sampler { get => alive(sampler); }
	internal GPUBindGroupRef BindGroup {
		get {
			chk();
			bindGroup ??= renderer.CreateTextureBindGroup(Texture, Sampler);
			return bindGroup.AsRef();
		}
	}

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

	// these three below should be fine as long as Texture calls alive()

	/// <summary>
	/// Gets the width of the texture in pixels.
	/// </summary>
	public uint Width => Texture.Width;

	/// <summary>
	/// Gets the height of the texture in pixels.
	/// </summary>
	public uint Height => Texture.Height;

	/// <summary>
	/// Gets the texture format.
	/// </summary>
	public TextureFormat Format => Texture.Format;

	private void chk() {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		if (Volatile.Read(ref revoked) != 0)
			throw new AssetLeaseExpiredException("borrowed texture was used after its lease expired");
	}

	private T alive<T>(T val) {
		chk();
		return val;
	}

	/// <summary>
	/// Revokes this texture, invalidating further use.
	/// </summary>
	/// <remarks>
	/// This is used by the asset subsystem when a leased <see cref="Texture2D"/> obtained
	/// by borrowing a <see cref="AssetRef{Texture2D}"/> expires. Revocation is logical
	/// invalidation only; the underlying GPU resources remain alive until <see cref="Dispose"/>.
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
