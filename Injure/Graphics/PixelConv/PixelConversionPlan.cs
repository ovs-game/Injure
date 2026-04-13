// SPDX-License-Identifier: MIT

using System;

namespace Injure.Graphics.PixelConv;

using unsafe Kernel = delegate *<ref readonly PixelConversionPlan, byte *, byte *, nuint, void>;

internal enum PlanKind : byte {
	ByteCopy,
	Copy32SetAlpha,
	Copy64SetAlpha,

	Shuffle32,
	Expand24To32,
	Contract32To24,

	Shuffle64,
	Widen32To64,
	Narrow64To32,

	Packed16To32,
	Unpacked32ToPacked16,

	Generic
}

public readonly unsafe struct PixelConversionPlan {
	internal readonly PlanKind Kind;
	internal readonly Kernel Kernel;

	internal readonly PixelFormatDesc SrcDesc;
	internal readonly PixelFormatDesc DstDesc;
	internal readonly PayloadUnion Payload;

	internal readonly byte Alpha8UNorm;
	internal readonly ushort Alpha16UNorm;

	public PixelFormat SourceFormat { get; }
	public PixelFormat DestinationFormat { get; }
	public PixelConvertOptions Options { get; }

	public int SourceBytesPerPixel => SrcDesc.BytesPerPixel;
	public int DestinationBytesPerPixel => DstDesc.BytesPerPixel;

	internal PixelConversionPlan(PixelFormat srcFmt, PixelFormat dstFmt, PixelConvertOptions opts, PlanKind kind,
		Kernel kernel, PixelFormatDesc srcDesc, PixelFormatDesc dstDesc, PayloadUnion payload) {
		SourceFormat = srcFmt;
		DestinationFormat = dstFmt;
		Options = opts;

		Kind = kind;
		Kernel = kernel;

		SrcDesc = srcDesc;
		DstDesc = dstDesc;
		Payload = payload;

		Alpha8UNorm = ConverterCore.Narrow16To8(opts.Alpha16UNorm);
		Alpha16UNorm = opts.Alpha16UNorm;
	}

	public void ConvertRow(ReadOnlySpan<byte> src, Span<byte> dst, int pxCount) {
		ArgumentOutOfRangeException.ThrowIfNegative(pxCount);
		if (pxCount == 0)
			return;

		int srcNeed = checked(pxCount * SrcDesc.BytesPerPixel);
		int dstNeed = checked(pxCount * DstDesc.BytesPerPixel);
		if (src.Length < srcNeed)
			throw new ArgumentException($"source span is smaller than expected (expected at least {srcNeed} bytes, got {src.Length})", nameof(src));
		if (dst.Length < dstNeed)
			throw new ArgumentException($"destination span is too small (need at least {dstNeed} bytes, got {dst.Length})", nameof(dst));

		if (Kind == PlanKind.ByteCopy) {
			src[..srcNeed].CopyTo(dst);
			return;
		}

		fixed (byte *pSrc = src)
		fixed (byte *pDst = dst) {
			Kernel(in this, pSrc, pDst, (nuint)pxCount);
		}
	}

	public void Convert(ReadOnlySpan<byte> src, int srcStride, Span<byte> dst, int dstStride, int width, int height) {
		ArgumentOutOfRangeException.ThrowIfNegative(srcStride);
		ArgumentOutOfRangeException.ThrowIfNegative(dstStride);
		ArgumentOutOfRangeException.ThrowIfNegative(width);
		ArgumentOutOfRangeException.ThrowIfNegative(height);
		if (width == 0 || height == 0)
			return;

		int srcRowBytes = checked(width * SourceBytesPerPixel);
		int dstRowBytes = checked(width * DestinationBytesPerPixel);
		if (srcStride < srcRowBytes)
			throw new ArgumentException("source stride is smaller than the row size", nameof(srcStride));
		if (dstStride < dstRowBytes)
			throw new ArgumentException("destination stride is smaller than the row size", nameof(dstStride));

		int srcNeed = checked((height - 1) * srcStride + srcRowBytes);
		int dstNeed = checked((height - 1) * dstStride + dstRowBytes);
		if (src.Length < srcNeed)
			throw new ArgumentException($"source span is smaller than expected (expected at least {srcNeed} bytes, got {src.Length})", nameof(src));
		if (dst.Length < dstNeed)
			throw new ArgumentException($"destination span is too small (need at least {dstNeed} bytes, got {dst.Length})", nameof(dst));
		for (int y = 0; y < height; y++)
			ConvertRow(src.Slice(y * srcStride, srcRowBytes), dst.Slice(y * dstStride, dstRowBytes), width);
	}
}
