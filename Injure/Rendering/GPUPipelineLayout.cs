// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Owning wrapper around a pipeline layout.
/// </summary>
public sealed unsafe class GPUPipelineLayout(WebGPUDevice device, PipelineLayout *pipelineLayout) : IDisposable {
	private readonly WebGPUDevice device = device;

	internal PipelineLayout *PipelineLayout { get; private set; } = pipelineLayout;

	/// <summary>
	/// Returns the underlying <see cref="Silk.NET.WebGPU.PipelineLayout"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public PipelineLayout *DangerousGetPtr() => PipelineLayout;

	/// <summary>
	/// Releases the underlying WebGPU pipeline layout.
	/// </summary>
	public void Dispose() {
		if (PipelineLayout is not null)
			device.API.PipelineLayoutRelease(PipelineLayout);
		PipelineLayout = null;
	}
}
