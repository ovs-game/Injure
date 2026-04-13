// SPDX-License-Identifier: MIT

using System;

using static Injure.Graphics.PixelConv.ConverterCore;

namespace Injure.Graphics.PixelConv;

using unsafe Kernel = delegate *<ref readonly PixelConversionPlan, byte *, byte *, nuint, void>;

public static unsafe class PixelConverter {
	public static PixelConversionPlan CreatePlan(PixelFormat srcFmt, PixelFormat dstFmt, PixelConvertOptions opts = default) {
		if (opts.IsDefault)
			opts = new PixelConvertOptions();
		PixelFormatDesc srcDesc = FormatDescs[srcFmt];
		PixelFormatDesc dstDesc = FormatDescs[dstFmt];

		if (WouldNarrow(in srcDesc, in dstDesc) && (opts.Flags & ConversionFlags.AllowNarrowing) == 0)
			throw new InvalidOperationException("conversion would narrow; refusing to convert without ConversionFlags.AllowNarrowing");
		if (WouldDropAlpha(in srcDesc, in dstDesc) && (opts.Flags & ConversionFlags.AllowDroppingAlpha) == 0)
			throw new InvalidOperationException("conversion would drop alpha; refusing to convert without ConversionFlags.AllowDroppingAlpha");
		if (WouldDropColorChannels(in srcDesc, in dstDesc) && (opts.Flags & ConversionFlags.AllowDroppingColorChannels) == 0)
			throw new InvalidOperationException("conversion would drop one or more color channels; refusing to convert without ConversionFlags.AllowDroppingColorChannels");

		PlanKind kind = Classify(srcFmt, dstFmt, in srcDesc, in dstDesc, in opts);
		PayloadUnion payload = MakePayload(kind, in srcDesc, in dstDesc, in opts);
		Kernel kernel = kind != PlanKind.ByteCopy ? KernelRegistry.Kernels[(int)kind].Pick() : null;
		return new PixelConversionPlan(srcFmt, dstFmt, opts, kind, kernel, srcDesc, dstDesc, payload);
	}

	public static void Convert(
		ReadOnlySpan<byte> src, int srcStride, PixelFormat srcFmt,
		Span<byte> dst, int dstStride, PixelFormat dstFmt,
		int width, int height,
		PixelConvertOptions opts = default
	) => CreatePlan(srcFmt, dstFmt, opts).Convert(src, srcStride, dst, dstStride, width, height);
}
