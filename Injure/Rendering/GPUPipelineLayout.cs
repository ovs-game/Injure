// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public sealed unsafe class GPUPipelineLayout(WebGPURenderer renderer, PipelineLayout *pipelineLayout) : IDisposable {
	private readonly WebGPURenderer renderer = renderer;

	internal PipelineLayout *PipelineLayout { get; private set; } = pipelineLayout;
	public PipelineLayout *DangerousGetPtr() => PipelineLayout;

	public void Dispose() {
		if (PipelineLayout is not null)
			renderer.webgpu.PipelineLayoutRelease(PipelineLayout);
		PipelineLayout = null;
	}
}
