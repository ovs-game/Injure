// SPDX-License-Identifier: MIT

using System;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for sampler wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GPUSamplerHandle {
	internal abstract WGPUSampler WGPUSampler { get; }

	/// <summary>
	/// Returns the underlying <see cref="WebGPU.WGPUSampler"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/Conventions/DangerousGet.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUSampler DangerousGetNative() => WGPUSampler;
}

/// <summary>
/// Owning wrapper around a sampler.
/// </summary>
public sealed class GPUSampler : GPUSamplerHandle, IDisposable {
	private WGPUSampler sampler;

	internal GPUSampler(WGPUSampler sampler) {
		this.sampler = sampler;
	}

	internal override WGPUSampler WGPUSampler => sampler;

	/// <summary>
	/// Releases the underlying WebGPU sampler object.
	/// </summary>
	public void Dispose() {
		if (sampler.IsNotNull)
			wgpuSamplerRelease(sampler);
		sampler = default;
	}
}

/// <summary>
/// Non-owning wrapper around a sampler.
/// </summary>
public sealed class GPUSamplerRef : GPUSamplerHandle {
	private readonly GPUSampler source;
	internal GPUSamplerRef(GPUSampler source) {
		this.source = source;
	}

	internal override WGPUSampler WGPUSampler => source.WGPUSampler;
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
