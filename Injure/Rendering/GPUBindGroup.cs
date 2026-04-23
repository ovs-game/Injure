// SPDX-License-Identifier: MIT

using System;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for bind group wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GPUBindGroupHandle {
	internal abstract WGPUBindGroup WGPUBindGroup { get; }

	/// <summary>
	/// Returns the underlying <see cref="WebGPU.WGPUBindGroup"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/Conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUBindGroup DangerousGetNative() => WGPUBindGroup;
}

/// <summary>
/// Owning wrapper around a bind group .
/// </summary>
public sealed class GPUBindGroup : GPUBindGroupHandle, IDisposable {
	private WGPUBindGroup bindGroup;

	internal GPUBindGroup(WGPUBindGroup bindGroup) {
		this.bindGroup = bindGroup;
	}

	internal override WGPUBindGroup WGPUBindGroup => bindGroup;

	/// <summary>
	/// Creates a non-owning view of this bind group.
	/// </summary>
	public GPUBindGroupRef AsRef() => new(this);

	/// <summary>
	/// Releases the underlying WebGPU bind group.
	/// </summary>
	public void Dispose() {
		if (bindGroup.IsNotNull)
			wgpuBindGroupRelease(bindGroup);
		bindGroup = default;
	}
}

/// <summary>
/// Non-owning wrapper around a bind group.
/// </summary>
public sealed class GPUBindGroupRef : GPUBindGroupHandle {
	private readonly GPUBindGroup source;
	internal GPUBindGroupRef(GPUBindGroup source) {
		this.source = source;
	}

	internal override WGPUBindGroup WGPUBindGroup => source.WGPUBindGroup;
}
