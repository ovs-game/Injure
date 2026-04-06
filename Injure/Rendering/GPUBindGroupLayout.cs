// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public abstract unsafe class GPUBindGroupLayoutHandle {
	internal abstract BindGroupLayout *BindGroupLayout { get; }
	public BindGroupLayout *DangerousGetPtr() => BindGroupLayout;
}

public sealed unsafe class GPUBindGroupLayout(WebGPUDevice device, BindGroupLayout *bindGroup) : GPUBindGroupLayoutHandle, IDisposable {
	private readonly WebGPUDevice device = device;
	private BindGroupLayout *p = bindGroup;

	internal override BindGroupLayout *BindGroupLayout => p;

	public GPUBindGroupLayoutRef AsRef() => new GPUBindGroupLayoutRef(this);

	public void Dispose() {
		if (p is not null)
			device.API.BindGroupLayoutRelease(p);
		p = null;
	}
}

public sealed unsafe class GPUBindGroupLayoutRef : GPUBindGroupLayoutHandle {
	private readonly GPUBindGroupLayout? source = null;
	private readonly BindGroupLayout *p = null;

	public GPUBindGroupLayoutRef(GPUBindGroupLayout source) {
		this.source = source;
	}
	public GPUBindGroupLayoutRef(BindGroupLayout *p) {
		this.p = p;
	}

	internal override BindGroupLayout *BindGroupLayout => (source is not null) ? source.BindGroupLayout : p;
}

