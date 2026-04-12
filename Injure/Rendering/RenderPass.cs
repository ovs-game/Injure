// SPDX-License-Identifier: MIT

using System;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

public sealed unsafe class RenderPass : IDisposable {
	private const ulong WholeSize = ulong.MaxValue;
	private readonly WGPURenderPassEncoder passEnc;
	private readonly Action onFinished;
	private bool disposed = false;

	internal RenderPass(WGPURenderPassEncoder passEnc, Action onFinished) {
		this.passEnc = passEnc;
		this.onFinished = onFinished;
	}

	public void SetPipeline(GPURenderPipelineHandle pipeline) {
		ObjectDisposedException.ThrowIf(disposed, this);
		wgpuRenderPassEncoderSetPipeline(passEnc, pipeline.WGPURenderPipeline);
	}

	public void SetBindGroup(uint index, GPUBindGroupHandle bindGroup) {
		ObjectDisposedException.ThrowIf(disposed, this);
		wgpuRenderPassEncoderSetBindGroup(passEnc, index, bindGroup.WGPUBindGroup, 0, null);
	}

	public void SetVertexBuffer(uint slot, GPUBufferHandle buffer, ulong offset = 0, ulong size = WholeSize) {
		ObjectDisposedException.ThrowIf(disposed, this);
		wgpuRenderPassEncoderSetVertexBuffer(passEnc, slot, buffer.WGPUBuffer, offset, size);
	}

	public void SetIndexBuffer(GPUBufferHandle buffer, IndexFormat format, ulong offset = 0, ulong size = WholeSize) {
		ObjectDisposedException.ThrowIf(disposed, this);
		wgpuRenderPassEncoderSetIndexBuffer(passEnc, buffer.WGPUBuffer, format.ToWebGPUType(), offset, size);
	}

	public void SetScissorRect(uint x, uint y, uint width, uint height) {
		ObjectDisposedException.ThrowIf(disposed, this);
		wgpuRenderPassEncoderSetScissorRect(passEnc, x, y, width, height);
	}

	public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) {
		ObjectDisposedException.ThrowIf(disposed, this);
		wgpuRenderPassEncoderDraw(passEnc, vertexCount, instanceCount, firstVertex, firstInstance);
	}

	public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) {
		ObjectDisposedException.ThrowIf(disposed, this);
		wgpuRenderPassEncoderDrawIndexed(passEnc, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		wgpuRenderPassEncoderEnd(passEnc);
		onFinished?.Invoke();
	}
}
