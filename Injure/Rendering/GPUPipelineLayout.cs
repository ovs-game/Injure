// SPDX-License-Identifier: MIT

using System;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for pipeline layout wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GPUPipelineLayoutHandle {
	internal abstract WGPUPipelineLayout WGPUPipelineLayout { get; }

	/// <summary>
	/// Returns the underlying <see cref="WebGPU.WGPUPipelineLayout"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/Conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUPipelineLayout DangerousGetNative() => WGPUPipelineLayout;
}

/// <summary>
/// Owning wrapper around a pipeline layout.
/// </summary>
public sealed class GPUPipelineLayout : GPUPipelineLayoutHandle, IDisposable {
	private WGPUPipelineLayout pipelineLayout;

	internal GPUPipelineLayout(WGPUPipelineLayout pipelineLayout) {
		this.pipelineLayout = pipelineLayout;
	}

	internal override WGPUPipelineLayout WGPUPipelineLayout => pipelineLayout;

	/// <summary>
	/// Creates a non-owning view of this pipeline layout.
	/// </summary>
	public GPUPipelineLayoutRef AsRef() => new(this);

	/// <summary>
	/// Releases the underlying WebGPU pipeline layout.
	/// </summary>
	public void Dispose() {
		if (pipelineLayout.IsNotNull)
			wgpuPipelineLayoutRelease(pipelineLayout);
		pipelineLayout = default;
	}
}

/// <summary>
/// Non-owning wrapper around a pipeline layout.
/// </summary>
public sealed class GPUPipelineLayoutRef : GPUPipelineLayoutHandle {
	private readonly GPUPipelineLayout source;
	internal GPUPipelineLayoutRef(GPUPipelineLayout source) {
		this.source = source;
	}

	internal override WGPUPipelineLayout WGPUPipelineLayout => source.WGPUPipelineLayout;
}
