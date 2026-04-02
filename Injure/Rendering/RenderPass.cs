// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public sealed unsafe class RenderPass(WebGPURenderer renderer, RenderPassEncoder *passEnc, Action onFinished) : IDisposable {
	private const ulong WholeSize = ulong.MaxValue;
	private readonly WebGPURenderer renderer = renderer;
	private readonly RenderPassEncoder *passEnc = passEnc;
	private readonly Action onFinished = onFinished;
	private bool disposed = false;

	public void SetPipeline(GPURenderPipeline pipeline) {
		ObjectDisposedException.ThrowIf(disposed, this);
		renderer.webgpu.RenderPassEncoderSetPipeline(passEnc, pipeline.Pipeline);
	}

	public void SetBindGroup(uint index, GPUBindGroupHandle bindGroup) {
		ObjectDisposedException.ThrowIf(disposed, this);
		renderer.webgpu.RenderPassEncoderSetBindGroup(passEnc, index, bindGroup.BindGroup, 0, null);
	}

	public void SetVertexBuffer(uint slot, GPUBuffer buffer, ulong offset = 0, ulong size = WholeSize) {
		ObjectDisposedException.ThrowIf(disposed, this);
		renderer.webgpu.RenderPassEncoderSetVertexBuffer(passEnc, slot, buffer.Buffer, offset, size);
	}

	public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format, ulong offset = 0, ulong size = WholeSize) {
		ObjectDisposedException.ThrowIf(disposed, this);
		renderer.webgpu.RenderPassEncoderSetIndexBuffer(passEnc, buffer.Buffer, format, offset, size);
	}

	public void SetScissorRect(uint x, uint y, uint width, uint height) {
		ObjectDisposedException.ThrowIf(disposed, this);
		renderer.webgpu.RenderPassEncoderSetScissorRect(passEnc, x, y, width, height);
	}

	public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) {
		ObjectDisposedException.ThrowIf(disposed, this);
		renderer.webgpu.RenderPassEncoderDraw(passEnc, vertexCount, instanceCount, firstVertex, firstInstance);
	}

	public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) {
		ObjectDisposedException.ThrowIf(disposed, this);
		renderer.webgpu.RenderPassEncoderDrawIndexed(passEnc, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		renderer.webgpu.RenderPassEncoderEnd(passEnc);
		onFinished?.Invoke();
	}
}
