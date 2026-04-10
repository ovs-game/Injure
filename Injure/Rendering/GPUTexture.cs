// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for texture wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract unsafe class GPUTextureHandle {
	internal abstract Texture *Texture { get; }

	/// <summary>
	/// Returns the underlying <see cref="Silk.NET.WebGPU.Texture"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public Texture *DangerousGetPtr() => Texture;

	/// <summary>
	/// Checks if another <see cref="GPUTextureHandle"/> points to the same
	/// underlying WebGPU texture object as this one.
	/// </summary>
	/// <remarks>
	/// If both this object and <paramref name="other"/> have been disposed and
	/// as such had the pointers to their underlying resources nulled out, always
	/// returns false even though <c>null == null</c> is technically true.
	/// </remarks>
	public bool SameTexture(GPUTextureHandle other) => other.Texture is not null && Texture == other.Texture;

	/// <summary>
	/// Gets the default texture view.
	/// </summary>
	public abstract GPUTextureViewRef DefaultView { get; }

	/// <summary>
	/// Width of the texture in texels.
	/// </summary>
	public abstract uint Width { get; }

	/// <summary>
	/// Height of the texture in texels.
	/// </summary>
	public abstract uint Height { get; }

	/// <summary>
	/// Depth of the texture in texels, or its array count.
	/// </summary>
	public abstract uint DepthOrArrayLayers { get; }

	/// <summary>
	/// Number of mip levels in the texture.
	/// </summary>
	public abstract uint MipLevelCount { get; }

	/// <summary>
	/// Sample count of the texture.
	/// </summary>
	public abstract uint SampleCount { get; }

	/// <summary>
	/// Texture dimension.
	/// </summary>
	public abstract TextureDimension Dimension { get; }

	/// <summary>
	/// Dimension used for the default view.
	/// </summary>
	/// <remarks>
	/// Matches the texture's dimension unless it's 2D and <see cref="DepthOrArrayLayers"/> is
	/// larger than 1, in which case this is <see cref="TextureViewDimension.Dimension2DArray"/>.
	/// </remarks>
	public abstract TextureViewDimension DefaultViewDimension { get; }

	/// <summary>
	/// Texture format.
	/// </summary>
	public abstract TextureFormat Format { get; }

	/// <summary>
	/// Allowed usages for this texture.
	/// </summary>
	public abstract TextureUsage Usage { get; }

	/// <summary>
	/// Additional formats that can be used for created views on this texture.
	/// </summary>
	public abstract ReadOnlySpan<TextureFormat> ViewFormats { get; }

	/// <summary>
	/// Creates a view for this texture, returning an owning object.
	/// </summary>
	/// <param name="params">View creation parameters.</param>
	public abstract GPUTextureView CreateView(in GPUTextureViewCreateParams @params);
}

/// <summary>
/// Owning wrapper around a GPU texture and its default view.
/// </summary>
public sealed unsafe class GPUTexture : GPUTextureHandle, IDisposable {
	private readonly WebGPUDevice device;
	private readonly GPUTextureView defaultView;
	private readonly TextureFormat[] viewFormats;
	private Texture *tex;

	internal GPUTexture(WebGPUDevice device, Texture *tex, uint width, uint height, uint depthOrArrayLayers,
		uint mipLevelCount, uint sampleCount, TextureDimension dimension, TextureFormat format, TextureUsage usage,
		TextureFormat[] viewFormats) {
		this.device = device;
		this.tex = tex;
		Width = width;
		Height = height;
		DepthOrArrayLayers = depthOrArrayLayers;
		MipLevelCount = mipLevelCount;
		SampleCount = sampleCount;
		Dimension = dimension;
		DefaultViewDimension = dimension switch {
			TextureDimension.Dimension1D => TextureViewDimension.Dimension1D,
			TextureDimension.Dimension2D => (depthOrArrayLayers > 1) ? TextureViewDimension.Dimension2DArray : TextureViewDimension.Dimension2D,
			TextureDimension.Dimension3D => TextureViewDimension.Dimension3D,
			_ => throw new UnreachableException()
		};
		Format = format;
		Usage = usage;
		this.viewFormats = viewFormats; // trust the caller and don't copy out the array
		defaultView = CreateView(new GPUTextureViewCreateParams());
		DefaultView = defaultView.AsRef();
	}

	internal override Texture *Texture => tex;
	public override GPUTextureViewRef DefaultView { get; }
	public override uint Width { get; }
	public override uint Height { get; }
	public override uint DepthOrArrayLayers { get; }
	public override uint MipLevelCount { get; }
	public override uint SampleCount { get; }
	public override TextureDimension Dimension { get; }
	public override TextureViewDimension DefaultViewDimension { get; }
	public override TextureFormat Format { get; }
	public override TextureUsage Usage { get; }
	public override ReadOnlySpan<TextureFormat> ViewFormats => viewFormats;

	public override GPUTextureView CreateView(in GPUTextureViewCreateParams @params) {
		TextureViewDescriptor desc = new TextureViewDescriptor {
			Format = @params.Format ?? default,
			Dimension = @params.Dimension ?? default,
			Aspect = @params.Aspect,
			BaseMipLevel = @params.BaseMipLevel,
			MipLevelCount = @params.MipLevelCount ?? default,
			BaseArrayLayer = @params.BaseArrayLayer,
			ArrayLayerCount = @params.ArrayLayerCount ?? default
		};
		TextureViewDimension dim = @params.Dimension ?? DefaultViewDimension;
		return new GPUTextureView(device, WebGPUException.Check(device.API.TextureCreateView(Texture, &desc)),
			@params.Format ?? (Format, @params.Aspect) switch {
				(TextureFormat.Depth24PlusStencil8, TextureAspect.DepthOnly) => TextureFormat.Depth24Plus,
				(TextureFormat.Depth24PlusStencil8, TextureAspect.StencilOnly) => TextureFormat.Stencil8,
				(TextureFormat.Depth32floatStencil8, TextureAspect.DepthOnly) => TextureFormat.Depth32float,
				(TextureFormat.Depth32floatStencil8, TextureAspect.StencilOnly) => TextureFormat.Stencil8,
				(_, TextureAspect.All) => Format,
				_ => throw new ArgumentException("texture format/aspect combination has no aspect-specific view format", nameof(@params))
			},
			dim,
			@params.Aspect,
			Usage,
			@params.BaseMipLevel,
			@params.MipLevelCount ?? (MipLevelCount - @params.BaseMipLevel),
			@params.BaseArrayLayer,
			@params.ArrayLayerCount ?? dim switch {
				TextureViewDimension.Dimension1D or TextureViewDimension.Dimension2D or TextureViewDimension.Dimension3D => 1,
				TextureViewDimension.DimensionCube => 6,
				TextureViewDimension.Dimension2DArray or TextureViewDimension.DimensionCubeArray => DepthOrArrayLayers - @params.BaseArrayLayer,
				_ => throw new UnreachableException()
			},
			Math.Max(1u, Width >> (int)@params.BaseMipLevel),
			dim == TextureViewDimension.Dimension1D ? 1u : Math.Max(1u, Height >> (int)@params.BaseMipLevel),
			dim != TextureViewDimension.Dimension3D ? 1u : Math.Max(1u, DepthOrArrayLayers >> (int)@params.BaseMipLevel),
			SampleCount
		);
	}

	/// <summary>
	/// Creates a non-owning view of this texture.
	/// </summary>
	public GPUTextureRef AsRef() => new GPUTextureRef(this);

	/// <summary>
	/// Disposes the default view and releases the underlying WebGPU texture.
	/// </summary>
	public void Dispose() {
		defaultView.Dispose();
		if (tex is not null)
			device.API.TextureRelease(tex);
		tex = null;
	}
}

/// <summary>
/// Non-owning wrapper around a GPU texture and its default view.
/// </summary>
public sealed unsafe class GPUTextureRef : GPUTextureHandle {
	private readonly GPUTexture source;
	internal GPUTextureRef(GPUTexture source) {
		this.source = source;
	}

	internal override Texture *Texture => source.Texture;
	public override GPUTextureViewRef DefaultView => source.DefaultView;
	public override uint Width => source.Width;
	public override uint Height => source.Height;
	public override uint DepthOrArrayLayers => source.DepthOrArrayLayers;
	public override uint MipLevelCount => source.MipLevelCount;
	public override uint SampleCount => source.SampleCount;
	public override TextureDimension Dimension => source.Dimension;
	public override TextureViewDimension DefaultViewDimension => source.DefaultViewDimension;
	public override TextureFormat Format => source.Format;
	public override TextureUsage Usage => source.Usage;
	public override ReadOnlySpan<TextureFormat> ViewFormats => source.ViewFormats;
	public override GPUTextureView CreateView(in GPUTextureViewCreateParams @params) => source.CreateView(in @params);
}

/// <summary>
/// Parameters used to create a <see cref="GPUTexture"/>.
/// </summary>
/// <param name="Width">Texture width in texels.</param>
/// <param name="Height">Texture height in texels.</param>
/// <param name="DepthOrArrayLayers">Texture depth in texels or array layer count.</param>
/// <param name="MipLevelCount">Number of mip levels.</param>
/// <param name="SampleCount">Texture sample count.</param>
/// <param name="Dimension">Texture dimension.</param>
/// <param name="Format">Texture format.</param>
/// <param name="Usage">Allowed usages for this texture.</param>
/// <param name="ViewFormats">Additional formats that can be used for created views on this texture.</param>
public readonly record struct GPUTextureCreateParams(
	uint Width,
	uint Height,
	uint DepthOrArrayLayers,
	uint MipLevelCount,
	uint SampleCount,
	TextureDimension Dimension,
	TextureFormat Format,
	TextureUsage Usage,
	ReadOnlyMemory<TextureFormat> ViewFormats = default
);

/// <summary>
/// Identifies a destination region and subresource within a texture.
/// </summary>
/// <param name="X">Destination X offset in texels.</param>
/// <param name="Y">Destination Y offset in texels.</param>
/// <param name="Z">Destination Z offset in texels or array layer base.</param>
/// <param name="Width">Width of the region in texels.</param>
/// <param name="Height">Height of the region in texels.</param>
/// <param name="DepthOrArrayLayers">Depth of the region in texels or array layer count.</param>
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
