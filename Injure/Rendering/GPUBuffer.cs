// SPDX-License-Identifier: MIT

using System;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for GPU buffer wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GPUBufferHandle {
	internal abstract WGPUBuffer WGPUBuffer { get; }

	/// <summary>
	/// Returns the underlying <see cref="WebGPU.WGPUBuffer"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/Conventions/DangerousGet.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUBuffer DangerousGetNative() => WGPUBuffer;

	/// <summary>
	/// Size of the buffer in bytes.
	/// </summary>
	public abstract ulong Size { get; }

	/// <summary>
	/// Allowed usages for this buffer.
	/// </summary>
	public abstract BufferUsage Usage { get; }
}

/// <summary>
/// Owning wrapper around a GPU buffer.
/// </summary>
public sealed class GPUBuffer : GPUBufferHandle, IDisposable {
	private WGPUBuffer buffer;

	internal GPUBuffer(WGPUBuffer buffer, ulong size, BufferUsage usage) {
		this.buffer = buffer;
		Size = size;
		Usage = usage;
	}

	internal override WGPUBuffer WGPUBuffer => buffer;
	public override ulong Size { get; }
	public override BufferUsage Usage { get; }

	/// <summary>
	/// Creates a non-owning view of this GPU buffer.
	/// </summary>
	public GPUBufferRef AsRef() => new GPUBufferRef(this);

	/// <summary>
	/// Releases the underlying WebGPU buffer.
	/// </summary>
	public void Dispose() {
		if (buffer.IsNotNull)
			wgpuBufferRelease(buffer);
		buffer = default;
	}
}

/// <summary>
/// Non-owning wrapper around a GPU buffer.
/// </summary>
public sealed class GPUBufferRef : GPUBufferHandle {
	private readonly GPUBuffer source;
	internal GPUBufferRef(GPUBuffer source) {
		this.source = source;
	}

	internal override WGPUBuffer WGPUBuffer => source.WGPUBuffer;
	public override ulong Size => source.Size;
	public override BufferUsage Usage => source.Usage;
}
