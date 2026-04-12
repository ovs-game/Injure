// SPDX-License-Identifier: MIT

using System;

namespace Injure.Rendering;

public sealed class ViewGlobals : IDisposable {
	private readonly WebGPUDevice device;
	private readonly GPUBuffer buffer;
	private readonly GPUBindGroup bindGroup;
	private bool disposed = false;

	public GPUBindGroupRef BindGroup {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return bindGroup.AsRef();
		}
	}

	public ViewGlobals(WebGPUDevice device, uint w, uint h) {
		this.device = device;
		buffer = device.CreateBuffer((ulong)GlobalsUniform.Size, BufferUsage.Uniform | BufferUsage.CopyDst);
		bindGroup = device.CreateUniformBufferBindGroup(device.StdGlobalsUniformLayout, buffer);
		Update(w, h);
	}

	public void Update(uint w, uint h) {
		ObjectDisposedException.ThrowIf(disposed, this);
		GlobalsUniform @params = new GlobalsUniform {
			Projection = MatrixUtil.OrthoTopLeft(w, h)
		};
		device.WriteToBuffer(buffer, 0, @params);
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		bindGroup.Dispose();
		buffer.Dispose();
	}
}
