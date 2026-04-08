// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Owning wrapper around an offscreen render target.
/// </summary>
public sealed unsafe class GPURenderTarget(WebGPUDevice device, Texture *colorTex, TextureView *colorView,
	Texture *depthStencilTex, TextureView *depthStencilView, uint w, uint h, TextureFormat colorFmt, TextureFormat? depthStencilFmt) : IDisposable {
	private readonly WebGPUDevice device = device;

	internal Texture *ColorTexture { get; private set; } = colorTex;
	internal TextureView *ColorView { get; private set; } = colorView;
	internal Texture *DepthStencilTexture { get; private set; } = depthStencilTex;
	internal TextureView *DepthStencilView { get; private set; } = depthStencilView;

	/// <summary>
	/// Returns the underlying color <see cref="Texture"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public Texture *DangerousGetColorTexturePtr() => ColorTexture;

	/// <summary>
	/// Returns the underlying color <see cref="TextureView"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public TextureView *DangerousGetColorViewPtr() => ColorView;

	/// <summary>
	/// Returns the underlying depth/stencil <see cref="Texture"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public Texture *DangerousGetDepthStencilTexturePtr() => DepthStencilTexture;

	/// <summary>
	/// Returns the underlying depth/stencil <see cref="TextureView"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public TextureView *DangerousGetDepthStencilViewPtr() => DepthStencilView;

	/// <summary>
	/// Width of the render target in texels.
	/// </summary>
	public uint Width { get; } = w;

	/// <summary>
	/// Height of the render target in texels.
	/// </summary>
	public uint Height { get; } = h;

	/// <summary>
	/// Format of the color attachment.
	/// </summary>
	public TextureFormat ColorFormat { get; } = colorFmt;

	/// <summary>
	/// Format of the depth/stencil attachment, or <see langword="null"/> if there isn't one.
	/// </summary>
	public TextureFormat? DepthStencilFormat { get; } = depthStencilFmt;

	/// <summary>
	/// Whether this render target has a depth attachment.
	/// </summary>
	[MemberNotNullWhen(true, nameof(DepthStencilFormat))]
	public bool HasDepth => DepthStencilFormat is not null;

	/// <summary>
	/// Whether this render target's depth attachment also includes stencil.
	/// </summary>
	[MemberNotNullWhen(true, nameof(DepthStencilFormat))]
	public bool HasStencil => DepthStencilFormat is TextureFormat.Depth24PlusStencil8 or TextureFormat.Depth32floatStencil8;

	/// <summary>
	/// Returns whether this render target and <paramref name="other"/> refer to
	/// the same underlying color attachment.
	/// </summary>
	public bool SameColorTarget(GPURenderTarget other) => other is not null && ColorView == other.ColorView;

	/// <summary>
	/// Releases the underlying color and, if present, depth/stencil attachment resources.
	/// </summary>
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
	TextureFormat ColorFormat,
	TextureFormat? DepthStencilFormat = null
);
