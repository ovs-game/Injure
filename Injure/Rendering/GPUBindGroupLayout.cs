// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for bind group layout wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract unsafe class GPUBindGroupLayoutHandle {
	internal abstract BindGroupLayout *BindGroupLayout { get; }

	/// <summary>
	/// Returns the underlying <see cref="Silk.NET.WebGPU.BindGroupLayout"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public BindGroupLayout *DangerousGetPtr() => BindGroupLayout;
}

/// <summary>
/// Owning wrapper around a bind group layout.
/// </summary>
public sealed unsafe class GPUBindGroupLayout : GPUBindGroupLayoutHandle, IDisposable {
	private readonly WebGPUDevice device;
	private BindGroupLayout *bindGroupLayout;

	internal GPUBindGroupLayout(WebGPUDevice device, BindGroupLayout *bindGroupLayout) {
		this.device = device;
		this.bindGroupLayout = bindGroupLayout;
	}

	internal override BindGroupLayout *BindGroupLayout => bindGroupLayout;

	/// <summary>
	/// Creates a non-owning view of this bind group layout.
	/// </summary>
	public GPUBindGroupLayoutRef AsRef() => new GPUBindGroupLayoutRef(this);

	/// <summary>
	/// Releases the underlying WebGPU bind group layout.
	/// </summary>
	public void Dispose() {
		if (bindGroupLayout is not null)
			device.API.BindGroupLayoutRelease(bindGroupLayout);
		bindGroupLayout = null;
	}
}

/// <summary>
/// Non-owning wrapper around a bind group layout.
/// </summary>
public sealed unsafe class GPUBindGroupLayoutRef : GPUBindGroupLayoutHandle {
	private readonly GPUBindGroupLayout source;
	internal GPUBindGroupLayoutRef(GPUBindGroupLayout source) {
		this.source = source;
	}

	internal override BindGroupLayout *BindGroupLayout => source.BindGroupLayout;
}
