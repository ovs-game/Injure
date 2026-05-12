// SPDX-License-Identifier: MIT

using System;

using Injure.Graphics.PixelConv;
using static Injure.Tests.Graphics.PixelConv.Util;

namespace Injure.Tests.Graphics.PixelConv;

public sealed class FullConversionTests {
	private const byte SrcGuard = 0xc7;
	private const byte DstGuard = 0x97;
	private const int PrefixPad = 11;
	private const int SuffixPad = 19;

	private static readonly PlanBackend[] Backends = [
		PlanBackend.AVX2,
		PlanBackend.SSSE3,
		PlanBackend.SSE2,
		PlanBackend.AdvSIMD,
		PlanBackend.Scalar,
	];

	public readonly record struct ConversionCase(
		string Name,
		ReferenceFamily Reference,
		PixelFormat SourceFormat,
		PixelFormat DestinationFormat,
		PixelConvertOptions Options,
		int Width,
		int Height
	);

	public static readonly TheoryData<ConversionCase> Cases = new() {
		new ConversionCase("Copy32SetAlpha_RGBA", ReferenceFamily.Copy32SetAlpha, PixelFormat.RGBA32_UNorm, PixelFormat.RGBA32_UNorm, new PixelConvertOptions { Alpha16UNorm = 0x1234, OverrideAlpha = true }, 257, 7),
		new ConversionCase("Copy32SetAlpha_ABGR", ReferenceFamily.Copy32SetAlpha, PixelFormat.ABGR32_UNorm, PixelFormat.ABGR32_UNorm, new PixelConvertOptions { Alpha16UNorm = 0xbeef, OverrideAlpha = true }, 255, 7),
		new ConversionCase("Copy64SetAlpha_RGBA64_LE", ReferenceFamily.Copy64SetAlpha, PixelFormat.RGBA64_UNorm_LE, PixelFormat.RGBA64_UNorm_LE, new PixelConvertOptions { Alpha16UNorm = 0x2468, OverrideAlpha = true }, 129, 5),
		new ConversionCase("Copy64SetAlpha_ARGB64_BE", ReferenceFamily.Copy64SetAlpha, PixelFormat.ARGB64_UNorm_BE, PixelFormat.ARGB64_UNorm_BE, new PixelConvertOptions { Alpha16UNorm = 0xc39a, OverrideAlpha = true }, 131, 5),
		new ConversionCase("Shuffle32_RGBA_to_BGRA", ReferenceFamily.Shuffle32, PixelFormat.RGBA32_UNorm, PixelFormat.BGRA32_UNorm, new PixelConvertOptions(), 257, 7),
		new ConversionCase("Shuffle32_ARGB_to_ABGR", ReferenceFamily.Shuffle32, PixelFormat.ARGB32_UNorm, PixelFormat.ABGR32_UNorm, new PixelConvertOptions(), 259, 5),
		new ConversionCase("Expand24To32_RGB_to_BGRA", ReferenceFamily.Expand24To32, PixelFormat.RGB24_UNorm, PixelFormat.BGRA32_UNorm, new PixelConvertOptions { Alpha16UNorm = 0x5aa5 }, 263, 6),
		new ConversionCase("Expand24To32_BGR_to_ARGB", ReferenceFamily.Expand24To32, PixelFormat.BGR24_UNorm, PixelFormat.ARGB32_UNorm, new PixelConvertOptions { Alpha16UNorm = 0x1337 }, 261, 6),
		new ConversionCase("Contract32To24_RGBA_to_RGB", ReferenceFamily.Contract32To24, PixelFormat.RGBA32_UNorm, PixelFormat.RGB24_UNorm, new PixelConvertOptions { Flags = ConversionFlags.AllowDroppingAlpha }, 263, 6),
		new ConversionCase("Contract32To24_ABGR_to_BGR", ReferenceFamily.Contract32To24, PixelFormat.ABGR32_UNorm, PixelFormat.BGR24_UNorm, new PixelConvertOptions { Flags = ConversionFlags.AllowDroppingAlpha }, 259, 6),
		new ConversionCase("Widen32To64_BGRA_to_ARGB64_LE", ReferenceFamily.Widen32To64, PixelFormat.BGRA32_UNorm, PixelFormat.ARGB64_UNorm_LE, new PixelConvertOptions(), 173, 5),
		new ConversionCase("Widen32To64_ARGB_to_RGBA64_BE", ReferenceFamily.Widen32To64, PixelFormat.ARGB32_UNorm, PixelFormat.RGBA64_UNorm_BE, new PixelConvertOptions(), 171, 5),
		new ConversionCase("Narrow64To32_RGBA64_LE_to_BGRA", ReferenceFamily.Narrow64To32, PixelFormat.RGBA64_UNorm_LE, PixelFormat.BGRA32_UNorm, new PixelConvertOptions { Flags = ConversionFlags.AllowNarrowing }, 173, 5),
		new ConversionCase("Narrow64To32_ABGR64_BE_to_ARGB", ReferenceFamily.Narrow64To32, PixelFormat.ABGR64_UNorm_BE, PixelFormat.ARGB32_UNorm, new PixelConvertOptions { Flags = ConversionFlags.AllowNarrowing }, 169, 5),
		new ConversionCase("Packed16To32_RGBA4444_LE_to_BGRA", ReferenceFamily.Packed16To32, PixelFormat.RGBA4444_UNormPack16_LE, PixelFormat.BGRA32_UNorm, new PixelConvertOptions(), 257, 6),
		new ConversionCase("Packed16To32_BGR565_BE_to_ARGB", ReferenceFamily.Packed16To32, PixelFormat.BGR565_UNormPack16_BE, PixelFormat.ARGB32_UNorm, new PixelConvertOptions { Alpha16UNorm = 0x8181 }, 255, 6),
		new ConversionCase("Unpacked32ToPacked16_BGRA_to_RGBA4444_BE", ReferenceFamily.Unpacked32ToPacked16, PixelFormat.BGRA32_UNorm, PixelFormat.RGBA4444_UNormPack16_BE, new PixelConvertOptions { Flags = ConversionFlags.AllowNarrowing }, 257, 6),
		new ConversionCase("Unpacked32ToPacked16_ARGB_to_BGR565_LE", ReferenceFamily.Unpacked32ToPacked16, PixelFormat.ARGB32_UNorm, PixelFormat.BGR565_UNormPack16_LE, new PixelConvertOptions { Flags = ConversionFlags.AllowNarrowing | ConversionFlags.AllowDroppingAlpha }, 255, 6),
	};

	[Theory]
	[MemberData(nameof(Cases))]
	public void ConversionsMatchReference(ConversionCase c) {
		assertCaseMatchesReference(c, padSourceRows: false, padDestinationRows: false);
	}

	[Theory]
	[MemberData(nameof(Cases))]
	public void ConversionsMatchReferenceWithPaddedRows(ConversionCase c) {
		assertCaseMatchesReference(c, padSourceRows: true, padDestinationRows: true);
	}

	private static void assertCaseMatchesReference(ConversionCase c, bool padSourceRows, bool padDestinationRows) {
		int srcRowBytes = checked(c.Width * GetBytesPerPixel(c.SourceFormat));
		int dstRowBytes = checked(c.Width * GetBytesPerPixel(c.DestinationFormat));
		int srcStride = srcRowBytes + (padSourceRows ? 13 : 0);
		int dstStride = dstRowBytes + (padDestinationRows ? 29 : 0);

		int srcBytes = checked(srcStride * c.Height);
		int dstBytes = checked(dstStride * c.Height);
		byte[] srcBuf = new byte[PrefixPad + srcBytes + SuffixPad];
		byte[] dstBuf = new byte[PrefixPad + dstBytes + SuffixPad];
		srcBuf.AsSpan().Fill(SrcGuard);
		dstBuf.AsSpan().Fill(DstGuard);
		patfill(srcBuf.AsSpan(PrefixPad, srcBytes), fnv(c.Name));

		ReadOnlySpan<byte> src = srcBuf.AsSpan(PrefixPad, srcBytes);
		byte[] reference = ReferenceConverter.Convert(c.Reference, src, srcStride, c.SourceFormat, c.DestinationFormat, c.Width, c.Height, c.Options);
		int referenceStride = dstRowBytes;

		bool sawAnything = false;
		foreach (PlanBackend backend in Backends) {
			if (!PixelConverter.TryCreatePlanWithBackend(c.SourceFormat, c.DestinationFormat, backend, out PixelConversionPlan plan, c.Options))
				continue;
			sawAnything = true;
			Assert.Equal(backend, plan.Info.Backend);
			Assert.Equal(PlanExecutionPath.DedicatedKernel, plan.Info.ExecutionPath);

			dstBuf.AsSpan().Fill(DstGuard);
			Span<byte> result = dstBuf.AsSpan(PrefixPad, dstBytes);
			plan.Convert(src, srcStride, result, dstStride, c.Width, c.Height);
			assertPxEqual(reference, result, referenceStride, dstStride, dstRowBytes, c.Height, c.Name, backend);
			assertGuards(srcBuf, SrcGuard, c.Name, backend, "src");
			assertGuards(dstBuf, DstGuard, c.Name, backend, "dst");
		}
		Assert.True(sawAnything, $"couldn't create any plans for case '{c.Name}'");
	}

	private static void assertPxEqual(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int aStride, int bStride, int rowBytes,
		int height, string caseName, PlanBackend backend) {
		for (int y = 0; y < height; y++) {
			ReadOnlySpan<byte> aRow = a.Slice(y * aStride, rowBytes);
			ReadOnlySpan<byte> bRow = b.Slice(y * bStride, rowBytes);
			Assert.True(aRow.SequenceEqual(bRow), $"pixel mismatch in case '{caseName}' backend {backend} row {y}");
		}
	}

	private static void assertGuards(byte[] buf, byte expected, string caseName, PlanBackend backend, string which) {
		for (int i = 0; i < PrefixPad; i++)
			Assert.True(buf[i] == expected, $"{which} prefix guard corrupted in case '{caseName}' backend {backend} index {i}");
		for (int i = buf.Length - SuffixPad; i < buf.Length; i++)
			Assert.True(buf[i] == expected, $"{which} suffix guard corrupted in case '{caseName}' backend {backend} index {i}");
	}

	private static void patfill(Span<byte> buf, uint seed) {
		uint s = seed;
		for (int i = 0; i < buf.Length; i++) {
			s ^= s << 13;
			s ^= s >> 17;
			s ^= s << 5;
			buf[i] = (byte)s;
		}
	}

	private static uint fnv(string s) {
		uint h = 2166136261u;
		for (int i = 0; i < s.Length; i++) {
			h ^= s[i];
			h *= 16777619u;
		}
		return h;
	}
}
