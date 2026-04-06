// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public sealed unsafe class GPURenderTarget(WebGPUDevice device, Texture *colorTex, TextureView *colorView,
	Texture *depthStencilTex, TextureView *depthStencilView, uint w, uint h, TextureFormat fmt) : IDisposable {
	private readonly WebGPUDevice device = device;

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
			device.API.TextureViewRelease(DepthStencilView);
		DepthStencilView = null;
		if (DepthStencilTexture is not null)
			device.API.TextureRelease(DepthStencilTexture);
		DepthStencilTexture = null;

		if (ColorView is not null)
			device.API.TextureViewRelease(ColorView);
		ColorView = null;
		if (ColorTexture is not null)
			device.API.TextureRelease(ColorTexture);
		ColorTexture = null;
	}
}

public readonly record struct GPURenderTargetCreateParams(
	uint Width,
	uint Height,
	TextureFormat Format,
	bool HasDepthStencil = false
);
