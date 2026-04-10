// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

using Buffer = Silk.NET.WebGPU.Buffer;

namespace Injure.Rendering;

/// <summary>
/// Common base type for GPU buffer wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract unsafe class GPUBufferHandle {
	internal abstract Buffer *Buffer { get; }

	/// <summary>
	/// Returns the underlying <see cref="Silk.NET.WebGPU.Buffer"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public Buffer *DangerousGetPtr() => Buffer;

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
public sealed unsafe class GPUBuffer : GPUBufferHandle, IDisposable {
	private readonly WebGPUDevice device;
	private Buffer *buffer;

	internal GPUBuffer(WebGPUDevice device, Buffer *buffer, ulong size, BufferUsage usage) {
		this.device = device;
		this.buffer = buffer;
		Size = size;
		Usage = usage;
	}

	internal override Buffer *Buffer => buffer;
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
		if (buffer is not null)
			device.API.BufferRelease(buffer);
		buffer = null;
	}
}

/// <summary>
/// Non-owning wrapper around a GPU buffer.
/// </summary>
public sealed unsafe class GPUBufferRef : GPUBufferHandle {
	private readonly GPUBuffer source;
	internal GPUBufferRef(GPUBuffer source) {
		this.source = source;
	}

	internal override Buffer *Buffer => source.Buffer;
	public override ulong Size => source.Size;
	public override BufferUsage Usage => source.Usage;
}
