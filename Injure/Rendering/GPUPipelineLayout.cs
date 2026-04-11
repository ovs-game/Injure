// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for pipeline layout wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract unsafe class GPUPipelineLayoutHandle {
	internal abstract PipelineLayout *PipelineLayout { get; }

	/// <summary>
	/// Returns the underlying <see cref="Silk.NET.WebGPU.PipelineLayout"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public PipelineLayout *DangerousGetPtr() => PipelineLayout;
}

/// <summary>
/// Owning wrapper around a pipeline layout.
/// </summary>
public sealed unsafe class GPUPipelineLayout : GPUPipelineLayoutHandle, IDisposable {
	private readonly WebGPUDevice device;
	private PipelineLayout *pipelineLayout;

	internal GPUPipelineLayout(WebGPUDevice device, PipelineLayout *pipelineLayout) {
		this.device = device;
		this.pipelineLayout = pipelineLayout;
	}

	internal override PipelineLayout *PipelineLayout => pipelineLayout;

	/// <summary>
	/// Creates a non-owning view of this pipeline layout.
	/// </summary>
	public GPUPipelineLayoutRef AsRef() => new GPUPipelineLayoutRef(this);

	/// <summary>
	/// Releases the underlying WebGPU pipeline layout.
	/// </summary>
	public void Dispose() {
		if (pipelineLayout is not null)
			device.API.PipelineLayoutRelease(pipelineLayout);
		pipelineLayout = null;
	}
}

/// <summary>
/// Non-owning wrapper around a pipeline layout.
/// </summary>
public sealed unsafe class GPUPipelineLayoutRef : GPUPipelineLayoutHandle {
	private readonly GPUPipelineLayout source;
	internal GPUPipelineLayoutRef(GPUPipelineLayout source) {
		this.source = source;
	}

	internal override PipelineLayout *PipelineLayout => source.PipelineLayout;
}
