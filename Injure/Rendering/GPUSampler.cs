// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public sealed unsafe class GPUSampler(WebGPURenderer renderer, Sampler *sampler) : IDisposable {
	private readonly WebGPURenderer renderer = renderer;

	internal Sampler *Sampler { get; private set; } = sampler;
	public Sampler *DangerousGetPtr() => Sampler;

	public void Dispose() {
		if (Sampler is not null)
			renderer.webgpu.SamplerRelease(Sampler);
		Sampler = null;
	}
}

public readonly record struct GPUSamplerCreateParams(
	FilterMode MinFilter,
	FilterMode MagFilter,
	MipmapFilterMode MipmapFilter,
	AddressMode AddressModeU,
	AddressMode AddressModeV,
	AddressMode AddressModeW
);
