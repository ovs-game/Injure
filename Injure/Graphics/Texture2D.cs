// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Injure.Assets;
using Injure.Graphics.PixelConv;
using Injure.Rendering;

namespace Injure.Graphics;

/// <summary>
/// High-level storage formats supported by <see cref="Texture2D"/>.
/// </summary>
public enum Texture2DFormat {
	R8_UNorm,
	RG16_UNorm,
	RGBA32_UNorm,
	RGBA32_UNorm_Srgb,
	BGRA32_UNorm,
	BGRA32_UNorm_Srgb
}

/// <summary>
/// High-level wrapper for a 2D color texture.
/// </summary>
/// <remarks>
/// <para>
/// Intentionally narrower than <see cref="GPUTexture"/>; always represents a single-layer,
/// non-multisampled, sampleable 2D color / mask texture with a default view and paired sampler.
/// Currently single-mip too, but that decision is not final.
/// </para>
/// <para>
/// If this <see cref="Texture2D"/> was obtained by borrowing an <see cref="AssetRef{Texture2D}"/>,
/// it will be revoked once that lease expires to make misuse more difficult. After revocation,
/// further usage attempts throw <see cref="AssetLeaseExpiredException"/>.
/// </para>
/// </remarks>
public sealed class Texture2D : IRevokable, IDisposable {
	private readonly WebGPUDevice device;
	private readonly GPUTexture texture;
	private readonly GPUSampler sampler;
	private GPUBindGroup? bindGroup = null;
	private int disposed = 0;
	private int revoked = 0;

	internal GPUTexture Texture { get { chk(); return texture; } }
	internal GPUSampler Sampler { get { chk(); return sampler; } }
	internal GPUBindGroupRef BindGroup { get { chk(); return (bindGroup ??= device.CreateStdColorTexture2DBindGroup(Texture, Sampler)).AsRef(); } }

	/// <summary>
	/// Returns the underlying <see cref="GPUTexture"/>, bypassing ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// See <c>Docs/Conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public GPUTexture DangerousGetGPUTexture() => Texture;

	/// <summary>
	/// Returns the underlying <see cref="GPUSampler"/>, bypassing ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// See <c>Docs/Conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public GPUSampler DangerousGetGPUSampler() => Sampler;

	/// <summary>
	/// Returns a standard color texture bind group with the texture's default
	/// view and sampler, bypassing ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// See <c>Docs/Conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public GPUBindGroupRef DangerousGetBindGroup() => BindGroup;

	/// <summary>
	/// Width of the texture in texels.
	/// </summary>
	public uint Width => Texture.Width;

	/// <summary>
	/// Height of the texture in texels.
	/// </summary>
	public uint Height => Texture.Height;

	/// <summary>
	/// High-level storage format of the texture.
	/// </summary>
	public Texture2DFormat Format { get; }

	/// <summary>
	/// Pixel-conversion destination format used for uploads to this texture.
	/// </summary>
	public PixelFormat UploadPixelFormat { get; }

	/// <summary>
	/// Creates an empty <see cref="Texture2D"/> with the given size and
	/// <see cref="Texture2DFormat.RGBA32_UNorm"/>.
	/// </summary>
	public Texture2D(WebGPUDevice device, uint width, uint height)
		: this(device, new Texture2DCreateParams(width, height)) {
	}

	/// <summary>
	/// Creates an empty <see cref="Texture2D"/> with the given size and format.
	/// </summary>
	public Texture2D(WebGPUDevice device, uint width, uint height, Texture2DFormat format)
		: this(device, new Texture2DCreateParams(width, height, format)) {
	}

	/// <summary>
	/// Creates an empty <see cref="Texture2D"/>.
	/// </summary>
	public Texture2D(WebGPUDevice device, in Texture2DCreateParams @params) {
		ArgumentNullException.ThrowIfNull(device);
		ArgumentOutOfRangeException.ThrowIfZero(@params.Width);
		ArgumentOutOfRangeException.ThrowIfZero(@params.Height);

		this.device = device;
		Format = @params.Format;
		UploadPixelFormat = getUploadPixelFormat(@params.Format);

		GPUTexture? texture = null;
		GPUSampler? sampler = null;
		try {
			texture = device.CreateTexture(new GPUTextureCreateParams(
				Width: @params.Width,
				Height: @params.Height,
				DepthOrArrayLayers: 1,
				MipLevelCount: 1,
				SampleCount: 1,
				Dimension: TextureDimension.Dimension2D,
				Format: getTextureFormat(@params.Format),
				Usage: TextureUsage.TextureBinding | TextureUsage.CopyDst
			));
			sampler = device.CreateSampler(@params.SamplerParams ?? SamplerStates.NearestClamp);
			this.texture = texture;
			this.sampler = sampler;
			texture = null;
			sampler = null;
		} finally {
			sampler?.Dispose();
			texture?.Dispose();
		}
	}

	/// <summary>
	/// Uploads pixel data to the entire texture.
	/// </summary>
	/// <param name="src">Source pixel data.</param>
	/// <param name="srcStride">Stride of <paramref name="src"/> in bytes (not pixels).</param>
	/// <param name="srcFmt">Pixel format of <paramref name="src"/>.</param>
	/// <param name="opts">Pixel conversion options.</param>
	public void Upload(ReadOnlySpan<byte> src, int srcStride, PixelFormat srcFmt, PixelConvertOptions opts = default) =>
		Upload(0, 0, src, srcStride, srcFmt, checked((int)Width), checked((int)Height), opts);

	/// <summary>
	/// Uploads pixel data to a sub-rectangle of the texture.
	/// </summary>
	/// <param name="x">Destination X offset in texels.</param>
	/// <param name="y">Destination Y offset in texels.</param>
	/// <param name="src">Source pixel data.</param>
	/// <param name="srcStride">Stride of <paramref name="src"/> in bytes (not pixels).</param>
	/// <param name="srcFmt">Pixel format of <paramref name="src"/>.</param>
	/// <param name="width">Width of the upload rectangle in texels.</param>
	/// <param name="height">Height of the upload rectangle in texels.</param>
	/// <param name="opts">Pixel conversion options.</param>
	public void Upload(int x, int y, ReadOnlySpan<byte> src, int srcStride, PixelFormat srcFmt, int width, int height, PixelConvertOptions opts = default) {
		chk();
		ArgumentOutOfRangeException.ThrowIfNegative(x);
		ArgumentOutOfRangeException.ThrowIfNegative(y);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(srcStride);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		if ((uint)x + (uint)width > Width || (uint)y + (uint)height > Height)
			throw new ArgumentException("upload rectangle is out of bounds of the texture");

		byte[] converted = PixelConverter.Convert(src, srcStride, srcFmt, UploadPixelFormat, out int dstStride, width, height, opts);
		device.WriteToTexture(
			texture,
			new GPUTextureRegion(
				X: (uint)x,
				Y: (uint)y,
				Z: 0,
				Width: (uint)width,
				Height: (uint)height,
				DepthOrArrayLayers: 1,
				MipLevel: 0
			),
			converted,
			new GPUTextureLayout(
				Offset: 0,
				BytesPerRow: (uint)dstStride,
				RowsPerImage: (uint)height
			)
		);
	}

	/// <summary>
	/// Revokes this texture, invalidating further use.
	/// </summary>
	/// <remarks>
	/// This is used by the asset system when a leased <see cref="Texture2D"/> obtained by
	/// borrowing a <see cref="AssetRef{Texture2D}"/> expires. Revocation is logical
	/// invalidation only; the underlying GPU resources remain alive until <see cref="Dispose"/>.
	/// </remarks>
	public void Revoke() {
		Volatile.Write(ref revoked, 1);
	}

	/// <summary>
	/// Releases the owned GPU resources.
	/// </summary>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;

		bindGroup?.Dispose();
		sampler.Dispose();
		texture.Dispose();
	}

	private static TextureFormat getTextureFormat(Texture2DFormat fmt) => fmt switch {
		Texture2DFormat.R8_UNorm          => TextureFormat.R8Unorm,
		Texture2DFormat.RG16_UNorm        => TextureFormat.RG8Unorm,
		Texture2DFormat.RGBA32_UNorm      => TextureFormat.RGBA8Unorm,
		Texture2DFormat.RGBA32_UNorm_Srgb => TextureFormat.RGBA8UnormSrgb,
		Texture2DFormat.BGRA32_UNorm      => TextureFormat.BGRA8Unorm,
		Texture2DFormat.BGRA32_UNorm_Srgb => TextureFormat.BGRA8UnormSrgb,
		_ => throw new UnreachableException()
	};

	private static PixelFormat getUploadPixelFormat(Texture2DFormat fmt) => fmt switch {
		Texture2DFormat.R8_UNorm          => PixelFormat.R8_UNorm,
		Texture2DFormat.RG16_UNorm        => PixelFormat.RG16_UNorm,
		Texture2DFormat.RGBA32_UNorm      => PixelFormat.RGBA32_UNorm,
		Texture2DFormat.RGBA32_UNorm_Srgb => PixelFormat.RGBA32_UNorm,
		Texture2DFormat.BGRA32_UNorm      => PixelFormat.BGRA32_UNorm,
		Texture2DFormat.BGRA32_UNorm_Srgb => PixelFormat.BGRA32_UNorm,
		_ => throw new UnreachableException()
	};

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void chk() {
		// check revoked first for more helpful use-after-lease-expiry exceptions
		if (Volatile.Read(ref revoked) != 0)
			throw new AssetLeaseExpiredException("borrowed texture was used after its lease expired");
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
	}
}

/// <summary>
/// Parameters used to create a <see cref="Texture2D"/>.
/// </summary>
/// <param name="Width">Texture width in texels.</param>
/// <param name="Height">Texture height in texels.</param>
/// <param name="Format">GPU storage format of the texture.</param>
/// <param name="SamplerParams">
/// Sampler creation parameters for the sampler created alongside the texture.
/// If <see langword="null"/>, <see cref="SamplerStates.NearestClamp"/> will be used.
/// </param>
public readonly record struct Texture2DCreateParams(
	uint Width,
	uint Height,
	Texture2DFormat Format = Texture2DFormat.RGBA32_UNorm,
	GPUSamplerCreateParams? SamplerParams = null
);
