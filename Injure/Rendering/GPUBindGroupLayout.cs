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
public sealed unsafe class GPUBindGroupLayout(WebGPUDevice device, BindGroupLayout *bindGroup) : GPUBindGroupLayoutHandle, IDisposable {
	private readonly WebGPUDevice device = device;
	private BindGroupLayout *p = bindGroup;

	internal override BindGroupLayout *BindGroupLayout => p;

	/// <summary>
	/// Creates a non-owning view of this bind group layout.
	/// </summary>
	public GPUBindGroupLayoutRef AsRef() => new GPUBindGroupLayoutRef(this);

	/// <summary>
	/// Releases the underlying WebGPU bind group layout.
	/// </summary>
	public void Dispose() {
		if (p is not null)
			device.API.BindGroupLayoutRelease(p);
		p = null;
	}
}

/// <summary>
/// Non-owning wrapper around a bind group layout.
/// </summary>
public sealed unsafe class GPUBindGroupLayoutRef : GPUBindGroupLayoutHandle {
	private readonly GPUBindGroupLayout? source = null;
	private readonly BindGroupLayout *p = null;

	/// <summary>
	/// Creates a source-backed bind group layout ref.
	/// </summary>
	public GPUBindGroupLayoutRef(GPUBindGroupLayout source) {
		this.source = source;
	}

	/// <summary>
	/// Creates a pointer-backed bind group layout ref.
	/// </summary>
	public GPUBindGroupLayoutRef(BindGroupLayout *p) {
		this.p = p;
	}

	internal override BindGroupLayout *BindGroupLayout => (source is not null) ? source.BindGroupLayout : p;
}
