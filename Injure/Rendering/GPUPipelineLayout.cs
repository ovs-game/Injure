// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public sealed unsafe class GPUPipelineLayout(WebGPUDevice device, PipelineLayout *pipelineLayout) : IDisposable {
	private readonly WebGPUDevice device = device;

	internal PipelineLayout *PipelineLayout { get; private set; } = pipelineLayout;
	public PipelineLayout *DangerousGetPtr() => PipelineLayout;

	public void Dispose() {
		if (PipelineLayout is not null)
			device.API.PipelineLayoutRelease(PipelineLayout);
		PipelineLayout = null;
	}
}
