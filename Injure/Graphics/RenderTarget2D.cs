// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using Silk.NET.WebGPU;

using Injure.Rendering;

namespace Injure.Graphics;

public sealed class RenderTarget2D : IDisposable {
	private readonly WebGPUDevice device;
	private int disposed = 0;

	public GPURenderTarget Target {
		get {
			ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
			return field;
		}
	}
	public GPUSampler Sampler {
		get {
			ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
			return field;
		}
	}
	public GPUBindGroupRef BindGroup {
		get {
			ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
			colorBindGroup ??= device.CreateTextureBindGroup(Target, Sampler);
			return colorBindGroup.AsRef();
		}
	}
	private GPUBindGroup? colorBindGroup = null;

	public uint Width => Target.Width;
	public uint Height => Target.Height;
	public TextureFormat Format => Target.Format;

	public RenderTarget2D(WebGPUDevice device, GPURenderTarget target, GPUSampler sampler) {
		this.device = device ?? throw new ArgumentNullException(nameof(device));
		Target = target ?? throw new ArgumentNullException(nameof(target));
		Sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
	}

	// "i don't care about the format" convenience
	public RenderTarget2D(WebGPUDevice device, uint width, uint height) : this(device, width, height, TextureFormat.Rgba8Unorm) {
	}

	public RenderTarget2D(WebGPUDevice device, uint width, uint height, TextureFormat format) {
		this.device = device ?? throw new ArgumentNullException(nameof(device));
		ArgumentOutOfRangeException.ThrowIfZero(width);
		ArgumentOutOfRangeException.ThrowIfZero(height);

		Target = device.CreateRenderTarget(new GPURenderTargetCreateParams(
			Width: width,
			Height: height,
			Format: format
		));
		Sampler = device.CreateSampler(SamplerStates.NearestClamp);
	}

	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;

		colorBindGroup?.Dispose();
		colorBindGroup = null;
		Sampler.Dispose();
		Target.Dispose();
	}
}
