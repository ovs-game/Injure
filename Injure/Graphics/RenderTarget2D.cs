// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

using Injure.Rendering;

namespace Injure.Graphics;

/// <summary>
/// High-level wrapper for an offscreen 2D render target.
/// </summary>
/// <remarks>
/// <para>
/// Owns a color texture, an optional depth/stencil texture, the color sampler,
/// and a lazy-created standard color texture bind group.
/// </para>
/// <para>
/// The color texture is always sampleable through <see cref="ColorView"/> and
/// <see cref="ColorBindGroup"/>. If a depth/stencil texture is present, its
/// attachment view is exposed through <see cref="DepthAttachmentView"/> and its
/// sampleable depth view is exposed through <see cref="DepthSampleView"/>.
/// </para>
/// </remarks>
public sealed class RenderTarget2D : IDisposable {
	private readonly WebGPUDevice device;
	private readonly GPUTexture colorTexture;
	private readonly GPUTexture? depthStencilTexture;
	private readonly GPUTextureView? depthSampleView; // only for depth+stencil formats
	private readonly GPUSampler colorSampler;
	private GPUBindGroup? colorBindGroup;
	private int disposed = 0;

	/// <summary>
	/// The color texture.
	/// </summary>
	public GPUTextureRef ColorTexture { get { chk(); return colorTexture.AsRef(); } }

	/// <summary>
	/// View for binding the color texture as a render attachment or sampling it.
	/// </summary>
	/// <remarks>
	/// This is the color texture's default view. Exists for convenience, and consistency
	/// with <see cref="DepthAttachmentView"/> / <see cref="DepthSampleView"/>.
	/// </remarks>
	public GPUTextureViewRef ColorView { get { chk(); return colorTexture.DefaultView; } }

	/// <summary>
	/// The optional depth/stencil texture.
	/// </summary>
	public GPUTextureRef? DepthStencilTexture { get { chk(); return depthStencilTexture?.AsRef(); } }

	/// <summary>
	/// View for binding the depth/stencil texture as a render attachment.
	/// </summary>
	/// <remarks>
	/// For depth-only textures, this is the texture's default view.
	/// For depth+stencil textures, this is the view with both depth and stencil.
	/// </remarks>
	public GPUTextureViewRef? DepthAttachmentView { get { chk(); return depthStencilTexture?.DefaultView; } }

	/// <summary>
	/// View for sampling the depth/stencil texture.
	/// </summary>
	/// <remarks>
	/// For depth-only textures, this is the texture's default view.
	/// For depth+stencil textures, this is a separate depth-only view.
	/// </remarks>
	public GPUTextureViewRef? DepthSampleView {
		get {
			chk();
			if (depthStencilTexture is null)
				return null;
			return depthSampleView?.AsRef() ?? depthStencilTexture.DefaultView;
		}
	}

	/// <summary>
	/// The color sampler, to be paired with <see cref="ColorView"/>.
	/// </summary>
	public GPUSampler ColorSampler { get { chk(); return colorSampler; } }

	/// <summary>
	/// Lazy-created standard color texture bind group for <see cref="ColorView"/>
	/// and <see cref="ColorSampler"/>.
	/// </summary>
	public GPUBindGroupRef ColorBindGroup { get { chk(); return (colorBindGroup ??= device.CreateStdColorTexture2DBindGroup(ColorView, ColorSampler)).AsRef(); } }

	/// <summary>
	/// Width of the render target in texels.
	/// </summary>
	public uint Width => colorTexture.Width;

	/// <summary>
	/// Height of the render target in texels.
	/// </summary>
	public uint Height => colorTexture.Height;

	/// <summary>
	/// Color attachment format.
	/// </summary>
	public TextureFormat ColorFormat => colorTexture.Format;

	/// <summary>
	/// Depth/stencil attachment format, if present.
	/// </summary>
	public TextureFormat? DepthStencilFormat => depthStencilTexture?.Format;

	/// <summary>
	/// Whether the render target has a depth/stencil attachment.
	/// </summary>
	[MemberNotNullWhen(true, nameof(DepthStencilTexture), nameof(DepthAttachmentView), nameof(DepthSampleView),
		nameof(DepthStencilFormat), nameof(depthStencilTexture))]
	public bool HasDepth => depthStencilTexture is not null;

	/// <summary>
	/// Whether the render target has a depth/stencil attachment with stencil.
	/// </summary>
	[MemberNotNullWhen(true, nameof(DepthStencilTexture), nameof(DepthAttachmentView), nameof(DepthSampleView),
		nameof(DepthStencilFormat), nameof(depthStencilTexture))]
	public bool HasStencil => depthStencilTexture is not null && formatHasStencil(depthStencilTexture.Format);

	/// <summary>
	/// Creates a new <see cref="RenderTarget2D"/>.
	/// </summary>
	public RenderTarget2D(WebGPUDevice device, in RenderTarget2DCreateParams @params) {
		this.device = device ?? throw new ArgumentNullException(nameof(device));
		ArgumentOutOfRangeException.ThrowIfZero(@params.Width);
		ArgumentOutOfRangeException.ThrowIfZero(@params.Height);

		GPUTexture? color = null;
		GPUTexture? depthStencil = null;
		GPUTextureView? depthSample = null;
		GPUSampler? sampler = null;
		try {
			color = device.CreateTexture(new GPUTextureCreateParams(
				Width: @params.Width,
				Height: @params.Height,
				DepthOrArrayLayers: 1,
				MipLevelCount: 1,
				SampleCount: 1,
				Dimension: TextureDimension.Dimension2D,
				Format: @params.ColorFormat,
				Usage: TextureUsage.RenderAttachment | TextureUsage.TextureBinding
			));
			if (@params.DepthStencilFormat is TextureFormat fmt) {
				depthStencil = device.CreateTexture(new GPUTextureCreateParams(
					Width: @params.Width,
					Height: @params.Height,
					DepthOrArrayLayers: 1,
					MipLevelCount: 1,
					SampleCount: 1,
					Dimension: TextureDimension.Dimension2D,
					Format: fmt,
					Usage: TextureUsage.RenderAttachment | TextureUsage.TextureBinding
				));
				// if the format has stencil we need a separate view for sampling
				depthSample = formatHasStencil(fmt) ? depthStencil.CreateView(new GPUTextureViewCreateParams(
					Aspect: TextureAspect.DepthOnly
				)) : null;
			}
			sampler = device.CreateSampler(@params.ColorSampler ?? SamplerStates.NearestClamp);
			colorTexture = color;
			depthStencilTexture = depthStencil;
			depthSampleView = depthSample;
			colorSampler = sampler;
		} catch {
			sampler?.Dispose();
			depthSample?.Dispose();
			depthStencil?.Dispose();
			color?.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Creates a standard filtering depth texture bind group for <see cref="DepthSampleView"/>.
	/// </summary>
	/// <param name="sampler">Sampler to use.</param>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this render target has no depth attachment.
	/// </exception>
	public GPUBindGroup CreateFilteringDepthBindGroup(GPUSamplerHandle sampler) {
		chk();
		GPUTextureViewRef view = DepthSampleView ?? throw new InvalidOperationException("render target has no depth attachment");
		return device.CreateStdFilteringDepthTexture2DBindGroup(view, sampler);
	}

	/// <summary>
	/// Creates a standard comparison depth texture bind group for <see cref="DepthSampleView"/>.
	/// </summary>
	/// <param name="sampler">Sampler to use.</param>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this render target has no depth attachment.
	/// </exception>
	public GPUBindGroup CreateComparisonDepthBindGroup(GPUSamplerHandle sampler) {
		chk();
		GPUTextureViewRef view = DepthSampleView ?? throw new InvalidOperationException("render target has no depth attachment");
		return device.CreateStdComparisonDepthTexture2DBindGroup(view, sampler);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void chk() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool formatHasStencil(TextureFormat format) =>
		format is TextureFormat.Depth24PlusStencil8 or TextureFormat.Depth32FloatStencil8 or TextureFormat.Stencil8;

	/// <summary>
	/// Releases the owned GPU resources.
	/// </summary>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		colorBindGroup?.Dispose();
		colorSampler.Dispose();
		depthSampleView?.Dispose();
		depthStencilTexture?.Dispose();
		colorTexture.Dispose();
	}
}

/// <summary>
/// Parameters used to create a <see cref="RenderTarget2D"/>.
/// </summary>
/// <param name="Width">Render target width in texels.</param>
/// <param name="Height">Render target height in texels.</param>
/// <param name="ColorFormat">Color attachment format.</param>
/// <param name="DepthStencilFormat">
/// Depth/stencil attachment format, or <see langword="null"/> to
/// not include a depth/stencil attachment.
/// </param>
/// <param name="ColorSampler">
/// Sampler parameters used for the standard sampled color view.
/// If <see langword="null"/>, the default value of
/// <see cref="SamplerStates.NearestClamp"/> is used.
/// </param>
public readonly record struct RenderTarget2DCreateParams(
	uint Width,
	uint Height,
	TextureFormat ColorFormat = TextureFormat.RGBA8Unorm,
	TextureFormat? DepthStencilFormat = null,
	GPUSamplerCreateParams? ColorSampler = null
);
