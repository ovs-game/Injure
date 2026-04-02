// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public abstract unsafe class GPUBindGroupHandle {
	internal abstract BindGroup *BindGroup { get; }
	public BindGroup *DangerousGetPtr() => BindGroup;
}

public sealed unsafe class GPUBindGroup(WebGPURenderer renderer, BindGroup *bindGroup) : GPUBindGroupHandle, IDisposable {
	private readonly WebGPURenderer renderer = renderer;
	private BindGroup *p = bindGroup;

	internal override BindGroup *BindGroup => p;

	public GPUBindGroupRef AsRef() => new GPUBindGroupRef(this);

	public void Dispose() {
		if (p is not null)
			renderer.webgpu.BindGroupRelease(p);
		p = null;
	}
}

public sealed unsafe class GPUBindGroupRef : GPUBindGroupHandle {
	private readonly GPUBindGroup? source = null;
	private readonly BindGroup *p = null;

	public GPUBindGroupRef(GPUBindGroup source) {
		this.source = source;
	}
	public GPUBindGroupRef(BindGroup *p) {
		this.p = p;
	}

	internal override BindGroup *BindGroup => (source is not null) ? source.BindGroup : p;
}
