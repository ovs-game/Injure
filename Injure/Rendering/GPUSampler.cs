// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Owning wrapper around a GPU sampler.
/// </summary>
public sealed unsafe class GPUSampler(WebGPUDevice device, Sampler *sampler) : IDisposable {
	private readonly WebGPUDevice device = device;

	internal Sampler *Sampler { get; private set; } = sampler;

	/// <summary>
	/// Returns the underlying <see cref="Silk.NET.WebGPU.Sampler"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public Sampler *DangerousGetPtr() => Sampler;

	/// <summary>
	/// Releases the underlying WebGPU sampler object.
	/// </summary>
	public void Dispose() {
		if (Sampler is not null)
			device.API.SamplerRelease(Sampler);
		Sampler = null;
	}
}

/// <summary>
/// Parameters used to create a <see cref="GPUSampler"/>.
/// </summary>
/// <param name="MinFilter">Minification filter.</param>
/// <param name="MagFilter">Magnification filter.</param>
/// <param name="MipmapFilter">Mipmap filter.</param>
/// <param name="AddressModeU">Address mode for the U axis.</param>
/// <param name="AddressModeV">Address mode for the V axis.</param>
/// <param name="AddressModeW">Address mode for the W axis.</param>
public readonly record struct GPUSamplerCreateParams(
	FilterMode MinFilter,
	FilterMode MagFilter,
	MipmapFilterMode MipmapFilter,
	AddressMode AddressModeU,
	AddressMode AddressModeV,
	AddressMode AddressModeW
);
