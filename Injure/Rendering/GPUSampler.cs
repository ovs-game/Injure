// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for sampler wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract unsafe class GPUSamplerHandle {
	internal abstract Sampler *Sampler { get; }

	/// <summary>
	/// Returns the underlying <see cref="Silk.NET.WebGPU.Sampler"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public Sampler *DangerousGetPtr() => Sampler;
}

/// <summary>
/// Owning wrapper around a sampler.
/// </summary>
public sealed unsafe class GPUSampler : GPUSamplerHandle, IDisposable {
	private readonly WebGPUDevice device;
	private Sampler *sampler;

	internal GPUSampler(WebGPUDevice device, Sampler *sampler) {
		this.device = device;
		this.sampler = sampler;
	}

	internal override Sampler *Sampler => sampler;

	/// <summary>
	/// Releases the underlying WebGPU sampler object.
	/// </summary>
	public void Dispose() {
		if (sampler is not null)
			device.API.SamplerRelease(sampler);
		sampler = null;
	}
}

/// <summary>
/// Non-owning wrapper around a sampler.
/// </summary>
public sealed unsafe class GPUSamplerRef : GPUSamplerHandle {
	private readonly GPUSampler source;
	internal GPUSamplerRef(GPUSampler source) {
		this.source = source;
	}

	internal override Sampler *Sampler => source.Sampler;
}

/// <summary>
/// Parameters used to create a <see cref="GPUSampler"/>.
/// </summary>
/// <param name="AddressModeU">Address mode for the U axis.</param>
/// <param name="AddressModeV">Address mode for the V axis.</param>
/// <param name="AddressModeW">Address mode for the W axis, if applicable.</param>
/// <param name="MagFilter">Magnification filter (behavior when the sampled area is smaller than or equal to one texel).</param>
/// <param name="MinFilter">Minification filter (behavior when the sampled area is larger than one texel).</param>
/// <param name="MipmapFilter">Mipmap filter (behavior when sampling between mipmap levels).</param>
/// <param name="LodMinClamp">Minimum level of detail used internally when sampling a texture.</param>
/// <param name="LodMaxClamp">Maximum level of detail used internally when sampling a texture.</param>
/// <param name="Compare">If provided, the sampler will be a comparison sampler using the specified function.</param>
/// <param name="MaxAnisotropy">Maximum anisotropy clamp value.</param>
/// <remarks>
/// <paramref name="MagFilter"/>, <paramref name="MinFilter"/>, and <paramref name="MipmapFilter"/> must
/// all be set to <see cref="FilterMode.Linear"/> (or <see cref="MipmapFilterMode.Linear"/>) if
/// <paramref name="MaxAnisotropy"/> is larger than 1.
/// </remarks>
public readonly record struct GPUSamplerCreateParams(
	AddressMode AddressModeU,
	AddressMode AddressModeV,
	AddressMode AddressModeW,
	FilterMode MagFilter,
	FilterMode MinFilter,
	MipmapFilterMode MipmapFilter,
	float LodMinClamp = 0f,
	float LodMaxClamp = 32f,
	CompareFunction Compare = default,
	ushort MaxAnisotropy = 1
);
