// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Injure.Graphics.PixelConv;

internal static unsafe class AVX2Kernels {
	public static void Copy32SetAlpha(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Copy32SetAlphaPayload pl = ref plan.Payload.Copy32SetAlpha;
		nuint px = pxCount & ~(nuint)0b111;
		nuint bytes = px * 4u;
		for (nuint i = 0; i < bytes; i += 32) {
			Vector256<byte> v = Unsafe.ReadUnaligned<Vector256<byte>>(src + i);
			v = Avx2.And(v, pl.KeepMask256);
			v = Avx2.Or(v, pl.FillMask256);
			Unsafe.WriteUnaligned(dst + i, v);
		}

		nuint tail = pxCount - px;
		if (tail > 0)
			ScalarKernels.Copy32SetAlpha(in plan, src + bytes, dst + bytes, tail);
	}

	public static void Copy64SetAlpha(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Copy64SetAlphaPayload pl = ref plan.Payload.Copy64SetAlpha;
		nuint px = pxCount & ~(nuint)0b11;
		nuint bytes = px * 8u;
		for (nuint i = 0; i < bytes; i += 32) {
			Vector256<byte> v = Unsafe.ReadUnaligned<Vector256<byte>>(src + i);
			v = Avx2.And(v, pl.KeepMask256);
			v = Avx2.Or(v, pl.FillMask256);
			Unsafe.WriteUnaligned(dst + i, v);
		}

		nuint tail = pxCount - px;
		if (tail > 0)
			ScalarKernels.Copy64SetAlpha(in plan, src + bytes, dst + bytes, tail);
	}

	public static void Shuffle32(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Shuffle32Payload pl = ref plan.Payload.Shuffle32;
		nuint px = pxCount & ~(nuint)0b111;
		nuint bytes = px * 4u;
		for (nuint i = 0; i < bytes; i += 32) {
			Vector256<byte> v = Unsafe.ReadUnaligned<Vector256<byte>>(src + i);
			v = Avx2.Shuffle(v, pl.ShufMask256);
			if (pl.HasFill)
				v = Avx2.Or(v, pl.FillMask256);
			Unsafe.WriteUnaligned(dst + i, v);
		}

		nuint tail = pxCount - px;
		if (tail > 0)
			ScalarKernels.Shuffle32(in plan, src + bytes, dst + bytes, tail);
	}

	public static void Expand24To32(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		// 3 bytes doesn't fit nicely into an SIMD lane so this one's kind of
		// odd-looking compared to the other ones here, do 2 16-byte loads and
		// have the masks ignore the unneeded bytes to read 24 bytes at a time

		ref readonly Expand24To32Payload pl = ref plan.Payload.Expand24To32;
		byte *s = src;
		byte *d = dst;
		nuint pixel = 0;
		// converts 8 pixels per iter, but since the first load does 0..15 and the second does
		// 12..27, it needs 28 bytes to not read past the end of the buffer, ceil(28/3) = 10
		for (; pxCount - pixel >= 10; pixel += 8, s += 24, d += 32) {
			// lo = [ 0,  1,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, XX, XX, XX, XX]
			// hi = [12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, XX, XX, XX, XX]
			Vector128<byte> lo = Unsafe.ReadUnaligned<Vector128<byte>>(s);
			Vector128<byte> hi = Unsafe.ReadUnaligned<Vector128<byte>>(s + 12);
			Vector256<byte> v = Vector256.Create(lo, hi);
			v = Avx2.Shuffle(v, pl.ShufMask256);
			v = Avx2.Or(v, pl.FillMask256);
			Unsafe.WriteUnaligned(d, v);
		}

		nuint tail = pxCount - pixel;
		if (tail > 0)
			ScalarKernels.Expand24To32(in plan, s, d, tail);
	}

	public static void Contract32To24(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Contract32To24Payload pl = ref plan.Payload.Contract32To24;
		nuint px = pxCount & ~(nuint)0b111;
		nuint bytes = px * 4u;
		nuint woff = 0;
		// process 32 bytes at a time and write 16+8 at a time
		for (nuint i = 0; i < bytes; i += 32, woff += 24) {
			Vector256<byte> v = Unsafe.ReadUnaligned<Vector256<byte>>(src + i);
			v = Avx2.Shuffle(v, pl.ShufMask256);
			Vector128<byte> lo = v.GetLower();
			Vector128<byte> hi = Avx2.ExtractVector128(v, 1);

			// first 16 bytes: lo[0..11] + hi[0..3]
			Vector128<byte> first16 = Sse2.Or(lo, Sse2.ShiftLeftLogical128BitLane(hi, 12));
			Unsafe.WriteUnaligned(dst + woff, first16);

			// last 8 bytes: hi[4..11]
			Vector128<byte> last8 = Sse2.ShiftRightLogical128BitLane(hi, 4);
			Unsafe.WriteUnaligned(dst + woff + 16, last8.AsUInt64().GetElement(0));
		}

		nuint tail = pxCount - px;
		if (tail > 0)
			ScalarKernels.Contract32To24(in plan, src + bytes, dst + woff, tail);
	}
}
