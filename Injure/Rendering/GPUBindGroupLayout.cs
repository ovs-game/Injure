// SPDX-License-Identifier: MIT

using System;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for bind group layout wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GPUBindGroupLayoutHandle {
	internal abstract WGPUBindGroupLayout WGPUBindGroupLayout { get; }

	/// <summary>
	/// Returns the underlying <see cref="WebGPU.WGPUBindGroupLayout"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/Conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUBindGroupLayout DangerousGetNative() => WGPUBindGroupLayout;
}

/// <summary>
/// Owning wrapper around a bind group layout.
/// </summary>
public sealed class GPUBindGroupLayout : GPUBindGroupLayoutHandle, IDisposable {
	private WGPUBindGroupLayout bindGroupLayout;

	internal GPUBindGroupLayout(WGPUBindGroupLayout bindGroupLayout) {
		this.bindGroupLayout = bindGroupLayout;
	}

	internal override WGPUBindGroupLayout WGPUBindGroupLayout => bindGroupLayout;

	/// <summary>
	/// Creates a non-owning view of this bind group layout.
	/// </summary>
	public GPUBindGroupLayoutRef AsRef() => new GPUBindGroupLayoutRef(this);

	/// <summary>
	/// Releases the underlying WebGPU bind group layout.
	/// </summary>
	public void Dispose() {
		if (bindGroupLayout.IsNotNull)
			wgpuBindGroupLayoutRelease(bindGroupLayout);
		bindGroupLayout = default;
	}
}

/// <summary>
/// Non-owning wrapper around a bind group layout.
/// </summary>
public sealed class GPUBindGroupLayoutRef : GPUBindGroupLayoutHandle {
	private readonly GPUBindGroupLayout source;
	internal GPUBindGroupLayoutRef(GPUBindGroupLayout source) {
		this.source = source;
	}

	internal override WGPUBindGroupLayout WGPUBindGroupLayout => source.WGPUBindGroupLayout;
}
