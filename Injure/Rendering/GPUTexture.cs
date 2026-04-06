// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public sealed unsafe class GPUTexture(WebGPUDevice device, Texture *tex, TextureView *view, uint w, uint h,
	TextureFormat fmt, TextureUsage usage, uint mipLevelCount, uint sampleCount, uint arrayLayerCount) : IDisposable {
	private readonly WebGPUDevice device = device;

	internal Texture *Texture { get; private set; } = tex;
	internal TextureView *View { get; private set; } = view;
	public Texture *DangerousGetTexturePtr() => Texture;
	public TextureView *DangerousGetViewPtr() => View;
	public readonly uint Width = w;
	public readonly uint Height = h;
	public readonly TextureFormat Format = fmt;
	public readonly TextureUsage Usage = usage;
	public readonly uint MipLevelCount = mipLevelCount;
	public readonly uint SampleCount = sampleCount;
	public readonly uint ArrayLayerCount = arrayLayerCount;

	public void Dispose() {
		if (View is not null)
			device.API.TextureViewRelease(View);
		View = null;
		if (Texture is not null)
			device.API.TextureRelease(Texture);
		Texture = null;
	}
}

public readonly record struct GPUTextureCreateParams(
	uint Width,
	uint Height,
	TextureFormat Format,
	TextureUsage Usage,
	uint MipLevelCount = 1,
	uint SampleCount = 1,
	uint ArrayLayerCount = 1
);

public readonly record struct GPUTextureRegion(
	uint X,
	uint Y,
	uint Z,
	uint Width,
	uint Height,
	uint DepthOrArrayLayers = 1,
	uint MipLevel = 0,
	TextureAspect Aspect = TextureAspect.All
);

public readonly record struct GPUTextureLayout(
	ulong Offset,
	uint BytesPerRow,
	uint RowsPerImage
);
