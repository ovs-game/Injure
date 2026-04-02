// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using Silk.NET.WebGPU;

using Injure.Rendering;

namespace Injure.Graphics;

public sealed class RenderTarget2D : IDisposable {
	private readonly WebGPURenderer renderer;
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
			colorBindGroup ??= renderer.CreateTextureBindGroup(Target, Sampler);
			return colorBindGroup.AsRef();
		}
	}
	private GPUBindGroup? colorBindGroup = null;

	public uint Width => Target.Width;
	public uint Height => Target.Height;
	public TextureFormat Format => Target.Format;

	public RenderTarget2D(WebGPURenderer renderer, GPURenderTarget target, GPUSampler sampler) {
		this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
		Target = target ?? throw new ArgumentNullException(nameof(target));
		Sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
	}

	// use `default` since if renderer is null it'll throw anyways before even getting to that arg
	public RenderTarget2D(WebGPURenderer renderer, uint width, uint height) : this(renderer, width, height, renderer?.BackbufferFormat ?? default) {
	}

	public RenderTarget2D(WebGPURenderer renderer, uint width, uint height, TextureFormat format) {
		this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
		ArgumentOutOfRangeException.ThrowIfZero(width);
		ArgumentOutOfRangeException.ThrowIfZero(height);

		Target = renderer.CreateRenderTarget(new GPURenderTargetCreateParams(
			Width: width,
			Height: height,
			Format: format
		));
		Sampler = renderer.CreateSampler(SamplerStates.NearestClamp);
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
