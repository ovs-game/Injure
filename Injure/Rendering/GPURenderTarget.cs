// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public sealed unsafe class GPURenderTarget(WebGPURenderer renderer, Texture *colorTex, TextureView *colorView,
	Texture *depthStencilTex, TextureView *depthStencilView, uint w, uint h, TextureFormat fmt) : IDisposable {
	private readonly WebGPURenderer renderer = renderer;

	internal Texture *ColorTexture { get; private set; } = colorTex;
	internal TextureView *ColorView { get; private set; } = colorView;
	internal Texture *DepthStencilTexture { get; private set; } = depthStencilTex;
	internal TextureView *DepthStencilView { get; private set; } = depthStencilView;
	public Texture *DangerousGetColorTexturePtr() => ColorTexture;
	public TextureView *DangerousGetColorViewPtr() => ColorView;
	public Texture *DangerousGetDepthStencilTexturePtr() => DepthStencilTexture;
	public TextureView *DangerousGetDepthStencilViewPtr() => DepthStencilView;
	public readonly uint Width = w;
	public readonly uint Height = h;
	public readonly TextureFormat Format = fmt;

	public bool SameColorTarget(GPURenderTarget other) => other is not null && ColorView == other.ColorView;

	public void Dispose() {
		if (DepthStencilView is not null)
			renderer.webgpu.TextureViewRelease(DepthStencilView);
		DepthStencilView = null;
		if (DepthStencilTexture is not null)
			renderer.webgpu.TextureRelease(DepthStencilTexture);
		DepthStencilTexture = null;

		if (ColorView is not null)
			renderer.webgpu.TextureViewRelease(ColorView);
		ColorView = null;
		if (ColorTexture is not null)
			renderer.webgpu.TextureRelease(ColorTexture);
		ColorTexture = null;
	}
}

public readonly record struct GPURenderTargetCreateParams(
	uint Width,
	uint Height,
	TextureFormat Format,
	bool HasDepthStencil = false
);
