// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

using Buffer = Silk.NET.WebGPU.Buffer;

namespace Injure.Rendering;

/// <summary>
/// Low-level owning wrapper around a GPU buffer.
/// </summary>
public sealed unsafe class GPUBuffer(WebGPURenderer renderer, Buffer *buffer, ulong size, BufferUsage usage) : IDisposable {
	private readonly WebGPURenderer renderer = renderer;

	internal Buffer *Buffer { get; private set; } = buffer;

	/// <summary>
	/// Returns the underlying pointer to a WebGPU <see cref="Silk.NET.WebGPU.Buffer"/>.
	/// </summary>
	/// <remarks>
	/// Returns <see langword="null"/> post-disposal.
	/// </remarks>
	public Buffer *DangerousGetPtr() => Buffer;

	/// <summary>
	/// Size of the buffer in bytes.
	/// </summary>
	public ulong Size { get; } = size;

	/// <summary>
	/// WebGPU usage flags for this buffer, set on creation.
	/// </summary>
	public BufferUsage Usage { get; } = usage;

	/// <summary>
	/// Releases the underlying WebGPU buffer.
	/// </summary>
	public void Dispose() {
		if (Buffer is not null)
			renderer.webgpu.BufferRelease(Buffer);
		Buffer = null;
	}
}
