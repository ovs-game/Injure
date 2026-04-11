// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for shader module wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract unsafe class GPUShaderModuleHandle {
	internal abstract ShaderModule *ShaderModule { get; }

	/// <summary>
	/// Returns the underlying <see cref="Silk.NET.WebGPU.ShaderModule"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public ShaderModule *DangerousGetPtr() => ShaderModule;
}

/// <summary>
/// Owning wrapper around a shader module.
/// </summary>
public sealed unsafe class GPUShaderModule : GPUShaderModuleHandle, IDisposable {
	private readonly WebGPUDevice device;
	private ShaderModule *shaderModule;

	internal GPUShaderModule(WebGPUDevice device, ShaderModule *shaderModule) {
		this.device = device;
		this.shaderModule = shaderModule;
	}

	internal override ShaderModule *ShaderModule => shaderModule;

	/// <summary>
	/// Creates a non-owning view of this shader module.
	/// </summary>
	public GPUShaderModuleRef AsRef() => new GPUShaderModuleRef(this);

	/// <summary>
	/// Releases the underlying WebGPU shader module.
	/// </summary>
	public void Dispose() {
		if (shaderModule is not null)
			device.API.ShaderModuleRelease(shaderModule);
		shaderModule = null;
	}
}

/// <summary>
/// Non-owning wrapper around a shader module.
/// </summary>
public sealed unsafe class GPUShaderModuleRef : GPUShaderModuleHandle {
	private readonly GPUShaderModule source;
	internal GPUShaderModuleRef(GPUShaderModule source) {
		this.source = source;
	}

	internal override ShaderModule *ShaderModule => source.ShaderModule;
}
