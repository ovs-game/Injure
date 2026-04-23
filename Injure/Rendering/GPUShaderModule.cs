// SPDX-License-Identifier: MIT

using System;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for shader module wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GPUShaderModuleHandle {
	internal abstract WGPUShaderModule WGPUShaderModule { get; }

	/// <summary>
	/// Returns the underlying <see cref="WebGPU.WGPUShaderModule"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/Conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUShaderModule DangerousGetNative() => WGPUShaderModule;
}

/// <summary>
/// Owning wrapper around a shader module.
/// </summary>
public sealed class GPUShaderModule : GPUShaderModuleHandle, IDisposable {
	private WGPUShaderModule shaderModule;

	internal GPUShaderModule(WGPUShaderModule shaderModule) {
		this.shaderModule = shaderModule;
	}

	internal override WGPUShaderModule WGPUShaderModule => shaderModule;

	/// <summary>
	/// Creates a non-owning view of this shader module.
	/// </summary>
	public GPUShaderModuleRef AsRef() => new(this);

	/// <summary>
	/// Releases the underlying WebGPU shader module.
	/// </summary>
	public void Dispose() {
		if (shaderModule.IsNotNull)
			wgpuShaderModuleRelease(shaderModule);
		shaderModule = default;
	}
}

/// <summary>
/// Non-owning wrapper around a shader module.
/// </summary>
public sealed class GPUShaderModuleRef : GPUShaderModuleHandle {
	private readonly GPUShaderModule source;
	internal GPUShaderModuleRef(GPUShaderModule source) {
		this.source = source;
	}

	internal override WGPUShaderModule WGPUShaderModule => source.WGPUShaderModule;
}
