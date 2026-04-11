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
/// Owning wrapper around a bind group.
/// </summary>
public sealed unsafe class GPUBindGroup : GPUBindGroupHandle, IDisposable {
	private readonly WebGPUDevice device;
	private BindGroup *bindGroup;

	internal GPUBindGroup(WebGPUDevice device, BindGroup *bindGroup) {
		this.device = device;
		this.bindGroup = bindGroup;
	}

	internal override BindGroup *BindGroup => bindGroup;

	/// <summary>
	/// Creates a non-owning view of this bind group.
	/// </summary>
	public GPUBindGroupRef AsRef() => new GPUBindGroupRef(this);

	/// <summary>
	/// Releases the underlying WebGPU bind group.
	/// </summary>
	public void Dispose() {
		if (bindGroup is not null)
			device.API.BindGroupRelease(bindGroup);
		bindGroup = null;
	}
}

/// <summary>
/// Non-owning wrapper around a bind group.
/// </summary>
public sealed unsafe class GPUBindGroupRef : GPUBindGroupHandle {
	private readonly GPUBindGroup source;
	internal GPUBindGroupRef(GPUBindGroup source) {
		this.source = source;
	}

	internal override BindGroup *BindGroup => source.BindGroup;
}
