// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Owning wrapper around a GPU texture and its default view.
/// </summary>
public sealed unsafe class GPUTexture(WebGPUDevice device, Texture *tex, TextureView *view, uint w, uint h,
	TextureFormat fmt, TextureUsage usage, uint mipLevelCount, uint sampleCount, uint arrayLayerCount) : IDisposable {
	private readonly WebGPUDevice device = device;

	internal Texture *Texture { get; private set; } = tex;
	internal TextureView *View { get; private set; } = view;

	/// <summary>
	/// Returns the underlying <see cref="Silk.NET.WebGPU.Texture"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public Texture *DangerousGetTexturePtr() => Texture;

	/// <summary>
	/// Returns the underlying default <see cref="TextureView"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public TextureView *DangerousGetViewPtr() => View;

	/// <summary>
	/// Width of the texture in texels.
	/// </summary>
	public uint Width { get; } = w;

	/// <summary>
	/// Height of the texture in texels.
	/// </summary>
	public uint Height { get; } = h;

	/// <summary>
	/// Texture format.
	/// </summary>
	public TextureFormat Format { get; } = fmt;

	/// <summary>
	/// WebGPU usage flags for this texture, set on creation.
	/// </summary>
	public TextureUsage Usage { get; } = usage;

	/// <summary>
	/// Number of mip levels in the texture.
	/// </summary>
	public uint MipLevelCount { get; } = mipLevelCount;

	/// <summary>
	/// Sample count of the texture.
	/// </summary>
	public uint SampleCount { get; } = sampleCount;

	/// <summary>
	/// Number of array layers in the texture.
	/// </summary>
	public uint ArrayLayerCount { get; } = arrayLayerCount;

	/// <summary>
	/// Releases the underlying WebGPU texture view and texture.
	/// </summary>
	public void Dispose() {
		if (View is not null)
			device.API.TextureViewRelease(View);
		View = null;
		if (Texture is not null)
			device.API.TextureRelease(Texture);
		Texture = null;
	}
}

/// <summary>
/// Parameters used to create a <see cref="GPUTexture"/>.
/// </summary>
/// <param name="Width">Texture width in texels.</param>
/// <param name="Height">Texture height in texels.</param>
/// <param name="Format">Texture format.</param>
/// <param name="Usage">WebGPU usage flags for the texture.</param>
/// <param name="MipLevelCount">Number of mip levels to create.</param>
/// <param name="SampleCount">Sample count for the texture.</param>
/// <param name="ArrayLayerCount">Number of array layers to create.</param>
public readonly record struct GPUTextureCreateParams(
	uint Width,
	uint Height,
	TextureFormat Format,
	TextureUsage Usage,
	uint MipLevelCount = 1,
	uint SampleCount = 1,
	uint ArrayLayerCount = 1
);

/// <summary>
/// Identifies a destination region and subresource within a texture.
/// </summary>
/// <param name="X">Destination X offset in texels.</param>
/// <param name="Y">Destination Y offset in texels.</param>
/// <param name="Z">Destination Z offset in texels or array-layer base.</param>
/// <param name="Width">Width of the region in texels.</param>
/// <param name="Height">Height of the region in texels.</param>
/// <param name="DepthOrArrayLayers">
/// Depth of the region in texels for 3D textures, or array-layer count for array textures.
/// </param>
/// <param name="MipLevel">Destination mip level.</param>
/// <param name="Aspect">Texture aspect to address.</param>
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

/// <summary>
/// Describes the source memory layout for a texture upload.
/// </summary>
/// <param name="Offset">Byte offset to the start of the upload data.</param>
/// <param name="BytesPerRow">
/// Number of bytes between successive rows. May be larger than the
/// number of bytes to be actually read from each row, allowing row
/// padding/stride in the data.
/// </param>
/// <param name="RowsPerImage">Number of rows between successive images or array layers.</param>
public readonly record struct GPUTextureLayout(
	ulong Offset,
	uint BytesPerRow,
	uint RowsPerImage
);
