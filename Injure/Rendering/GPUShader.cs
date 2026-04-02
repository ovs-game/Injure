// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public sealed unsafe class GPUShader(WebGPURenderer renderer, ShaderModule *shaderModule) : IDisposable {
	private readonly WebGPURenderer renderer = renderer;

	internal ShaderModule *ShaderModule { get; private set; } = shaderModule;
	public ShaderModule *DangerousGetPtr() => ShaderModule;

	public void Dispose() {
		if (ShaderModule is not null)
			renderer.webgpu.ShaderModuleRelease(ShaderModule);
		ShaderModule = null;
	}
}
