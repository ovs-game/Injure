// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public sealed unsafe class GPUShader(WebGPUDevice device, ShaderModule *shaderModule) : IDisposable {
	private readonly WebGPUDevice device = device;

	internal ShaderModule *ShaderModule { get; private set; } = shaderModule;
	public ShaderModule *DangerousGetPtr() => ShaderModule;

	public void Dispose() {
		if (ShaderModule is not null)
			device.API.ShaderModuleRelease(ShaderModule);
		ShaderModule = null;
	}
}
