// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public sealed unsafe class RenderPass : IDisposable {
	private const ulong WholeSize = ulong.MaxValue;
	private readonly WebGPUDevice device;
	private readonly RenderPassEncoder *passEnc;
	private readonly Action onFinished;
	private bool disposed = false;

	internal RenderPass(WebGPUDevice device, RenderPassEncoder *passEnc, Action onFinished) {
		this.device = device;
		this.passEnc = passEnc;
		this.onFinished = onFinished;
	}

	public void SetPipeline(GPURenderPipelineHandle pipeline) {
		ObjectDisposedException.ThrowIf(disposed, this);
		device.API.RenderPassEncoderSetPipeline(passEnc, pipeline.RenderPipeline);
	}

	public void SetBindGroup(uint index, GPUBindGroupHandle bindGroup) {
		ObjectDisposedException.ThrowIf(disposed, this);
		device.API.RenderPassEncoderSetBindGroup(passEnc, index, bindGroup.BindGroup, 0, null);
	}

	public void SetVertexBuffer(uint slot, GPUBufferHandle buffer, ulong offset = 0, ulong size = WholeSize) {
		ObjectDisposedException.ThrowIf(disposed, this);
		device.API.RenderPassEncoderSetVertexBuffer(passEnc, slot, buffer.Buffer, offset, size);
	}

	public void SetIndexBuffer(GPUBufferHandle buffer, IndexFormat format, ulong offset = 0, ulong size = WholeSize) {
		ObjectDisposedException.ThrowIf(disposed, this);
		device.API.RenderPassEncoderSetIndexBuffer(passEnc, buffer.Buffer, format, offset, size);
	}

	public void SetScissorRect(uint x, uint y, uint width, uint height) {
		ObjectDisposedException.ThrowIf(disposed, this);
		device.API.RenderPassEncoderSetScissorRect(passEnc, x, y, width, height);
	}

	public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) {
		ObjectDisposedException.ThrowIf(disposed, this);
		device.API.RenderPassEncoderDraw(passEnc, vertexCount, instanceCount, firstVertex, firstInstance);
	}

	public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) {
		ObjectDisposedException.ThrowIf(disposed, this);
		device.API.RenderPassEncoderDrawIndexed(passEnc, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		device.API.RenderPassEncoderEnd(passEnc);
		onFinished?.Invoke();
	}
}
