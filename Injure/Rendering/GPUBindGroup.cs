// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for bind group wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract unsafe class GPUBindGroupHandle {
	internal abstract BindGroup *BindGroup { get; }

	/// <summary>
	/// Returns the underlying <see cref="Silk.NET.WebGPU.BindGroup"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public BindGroup *DangerousGetPtr() => BindGroup;
}

/// <summary>
/// Owning wrapper around a GPU bind group.
/// </summary>
public sealed unsafe class GPUBindGroup(WebGPUDevice device, BindGroup *bindGroup) : GPUBindGroupHandle, IDisposable {
	private readonly WebGPUDevice device = device;
	private BindGroup *p = bindGroup;

	internal override BindGroup *BindGroup => p;

	/// <summary>
	/// Creates a non-owning view of this bind group.
	/// </summary>
	public GPUBindGroupRef AsRef() => new GPUBindGroupRef(this);

	/// <summary>
	/// Releases the underlying WebGPU bind group.
	/// </summary>
	public void Dispose() {
		if (p is not null)
			device.API.BindGroupRelease(p);
		p = null;
	}
}

/// <summary>
/// Non-owning wrapper around a GPU bind group.
/// </summary>
public sealed unsafe class GPUBindGroupRef : GPUBindGroupHandle {
	private readonly GPUBindGroup? source = null;
	private readonly BindGroup *p = null;

	/// <summary>
	/// Creates a source-backed bind group ref.
	/// </summary>
	public GPUBindGroupRef(GPUBindGroup source) {
		this.source = source;
	}

	/// <summary>
	/// Creates a pointer-backed bind group ref.
	/// </summary>
	public GPUBindGroupRef(BindGroup *p) {
		this.p = p;
	}

	internal override BindGroup *BindGroup => (source is not null) ? source.BindGroup : p;
}
