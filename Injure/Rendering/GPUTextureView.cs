// SPDX-License-Identifier: MIT

using System;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for texture view wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GPUTextureViewHandle {
	internal abstract WGPUTextureView WGPUTextureView { get; }

	/// <summary>
	/// Returns the underlying <see cref="WebGPU.WGPUTextureView"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/Conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUTextureView DangerousGetNative() => WGPUTextureView;

	/// <summary>
	/// Format of the view.
	/// </summary>
	public abstract TextureFormat Format { get; }

	/// <summary>
	/// Dimension of the view.
	/// </summary>
	public abstract TextureViewDimension Dimension { get; }

	/// <summary>
	/// Which aspects of the texture are accessible to the view.
	/// </summary>
	public abstract TextureAspect Aspect { get; }

	/// <summary>
	/// Allowed usages for this view.
	/// </summary>
	/// <remarks>
	/// Currently simply mirrors the texture's usages; proper support is
	/// planned.
	/// </remarks>
	public abstract TextureUsage Usage { get; }

	/// <summary>
	/// Base (most detailed) mipmap level accessible to the view.
	/// </summary>
	public abstract uint BaseMipLevel { get; }

	/// <summary>
	/// How many mipmap levels, starting with <see cref="BaseMipLevel"/>, are
	/// accessible to the view.
	/// </summary>
	public abstract uint MipLevelCount { get; }

	/// <summary>
	/// First array layer accessible to the view.
	/// </summary>
	public abstract uint BaseArrayLayer { get; }

	/// <summary>
	/// How many array layers, starting with <see cref="BaseArrayLayer"/>, are
	/// accessible to the view.
	/// </summary>
	public abstract uint ArrayLayerCount { get; }

	/// <summary>
	/// Width of the view.
	/// </summary>
	public abstract uint Width { get; }

	/// <summary>
	/// Height of the view (1 for 1D views).
	/// </summary>
	public abstract uint Height { get; }

	/// <summary>
	/// Depth of the view (1 for non-3D views).
	/// </summary>
	public abstract uint Depth { get; }

	/// <summary>
	/// Sample count of the view.
	/// </summary>
	public abstract uint SampleCount { get; }
}

/// <summary>
/// Owning wrapper around a texture view.
/// </summary>
public sealed class GPUTextureView : GPUTextureViewHandle, IDisposable {
	private WGPUTextureView texView;

	internal GPUTextureView(WGPUTextureView texView, TextureFormat format, TextureViewDimension dimension,
		TextureAspect aspect, TextureUsage usage, uint baseMipLevel, uint mipLevelCount, uint baseArrayLayer, uint arrayLayerCount,
		uint width, uint height, uint depth, uint sampleCount) {
		this.texView = texView;
		Format = format;
		Dimension = dimension;
		Aspect = aspect;
		Usage = usage;
		BaseMipLevel = baseMipLevel;
		MipLevelCount = mipLevelCount;
		BaseArrayLayer = baseArrayLayer;
		ArrayLayerCount = arrayLayerCount;
		Width = width;
		Height = height;
		Depth = depth;
		SampleCount = sampleCount;
	}

	internal override WGPUTextureView WGPUTextureView => texView;
	public override TextureFormat Format { get; }
	public override TextureViewDimension Dimension { get; }
	public override TextureAspect Aspect { get; }
	public override TextureUsage Usage { get; }
	public override uint BaseMipLevel { get; }
	public override uint MipLevelCount { get; }
	public override uint BaseArrayLayer { get; }
	public override uint ArrayLayerCount { get; }
	public override uint Width { get; }
	public override uint Height { get; }
	public override uint Depth { get; }
	public override uint SampleCount { get; }

	/// <summary>
	/// Creates a non-owning view of this texture view.
	/// </summary>
	public GPUTextureViewRef AsRef() => new(this);

	/// <summary>
	/// Releases the underlying WebGPU texture view.
	/// </summary>
	public void Dispose() {
		if (texView.IsNotNull)
			wgpuTextureViewRelease(texView);
		texView = default;
	}
}

/// <summary>
/// Non-owning wrapper around a texture view.
/// </summary>
public sealed class GPUTextureViewRef : GPUTextureViewHandle {
	private readonly GPUTextureView source;
	internal GPUTextureViewRef(GPUTextureView source) {
		this.source = source;
	}

	internal override WGPUTextureView WGPUTextureView => source.WGPUTextureView;
	public override TextureFormat Format => source.Format;
	public override TextureViewDimension Dimension => source.Dimension;
	public override TextureAspect Aspect => source.Aspect;
	public override TextureUsage Usage => source.Usage;
	public override uint BaseMipLevel => source.BaseMipLevel;
	public override uint MipLevelCount => source.MipLevelCount;
	public override uint BaseArrayLayer => source.BaseArrayLayer;
	public override uint ArrayLayerCount => source.ArrayLayerCount;
	public override uint Width => source.Width;
	public override uint Height => source.Height;
	public override uint Depth => source.Depth;
	public override uint SampleCount => source.SampleCount;
}

/// <summary>
/// Parameters used to create a <see cref="GPUTextureView"/>.
/// </summary>
/// <param name="Format">
/// <para>
/// If <see cref="Aspect"/> is <see cref="TextureAspect.All"/>, this must be either
/// the texture's format or one of its <see cref="GPUTextureCreateParams.ViewFormats"/>.
/// If <see cref="Aspect"/> is <see cref="TextureAspect.DepthOnly"/> or
/// <see cref="TextureAspect.StencilOnly"/>, this must be the corresponding
/// aspect-specific format of the texture's depth/stencil format.
/// </para>
/// <para>
/// Can be <see langword="null"/> to use the default of:
/// <list type="bullet">
/// <item><description>The texture's format for <see cref="TextureAspect.All"/>.</description></item>
/// <item><description>
/// <see cref="TextureFormat.Depth24Plus"/> or <see cref="TextureFormat.Depth32Float"/>
/// for depth-only views of combined depth+stencil textures.
/// </description></item>
/// <item><description>
/// <see cref="TextureFormat.Stencil8"/> for stencil-only views of combined
/// depth+stencil textures.
/// </description></item>
/// </list>
/// Any other omitted-format combination is invalid and causes view creation to throw.
/// </para>
/// </param>
/// <param name="Dimension">
/// The dimension to view the texture as, or <see langword="null"/> to derive the
/// dimension from the texture.
/// </param>
/// <param name="Aspect">Which aspects of the texture are accessible to the view.</param>
/// <param name="BaseMipLevel">Base (most detailed) mipmap level accessible to the view.</param>
/// <param name="MipLevelCount">
/// How many mipmap levels, starting with <paramref name="BaseMipLevel"/>, are
/// accessible to the view, or <see langword="null"/> to use all remaining mip levels.
/// </param>
/// <param name="BaseArrayLayer">First array layer accessible to the view.</param>
/// <param name="ArrayLayerCount">
/// How many array layers, starting with <paramref name="BaseArrayLayer"/>, are
/// accessible to the view, or <see langword="null"/> to use the default of:
/// <list type="bullet">
/// <item><description><c>1</c> for <c>1d</c>, <c>2d</c>, or <c>3d</c> views.</description></item>
/// <item><description><c>6</c> for <c>cube</c> views.</description></item>
/// <item><description><c>texture.DepthOrArrayLayers - BaseArrayLayer</c> for <c>2d-array</c> or <c>cube-array</c> views.</description></item>
/// </list>
/// </param>
/// <remarks>
/// Usage flags are currently not settable; proper support is planned.
/// </remarks>
public readonly record struct GPUTextureViewCreateParams(
	TextureFormat? Format,
	TextureViewDimension? Dimension,
	TextureAspect Aspect,
	uint BaseMipLevel = 0,
	uint? MipLevelCount = null,
	uint BaseArrayLayer = 0,
	uint? ArrayLayerCount = null
);
