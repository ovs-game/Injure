// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

namespace Injure.Graphics.PixelConv;

internal static unsafe class AdvSIMDKernels {
	public static void Copy32SetAlpha(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Copy32SetAlphaPayload pl = ref plan.Payload.Copy32SetAlpha;
		nuint px = pxCount & ~(nuint)0b11;
		nuint bytes = px * 4u;
		for (nuint i = 0; i < bytes; i += 16) {
			Vector128<byte> v = AdvSimd.LoadVector128(src + i);
			v = AdvSimd.And(v, pl.KeepMask128);
			v = AdvSimd.Or(v, pl.FillMask128);
			Unsafe.WriteUnaligned(dst + i, v);
		}

		nuint tail = pxCount - px;
		if (tail > 0)
			ScalarKernels.Copy32SetAlpha(in plan, src + bytes, dst + bytes, tail);
	}

	public static void Copy64SetAlpha(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Copy64SetAlphaPayload pl = ref plan.Payload.Copy64SetAlpha;
		nuint px = pxCount & ~(nuint)0b1;
		nuint bytes = px * 8u;
		for (nuint i = 0; i < bytes; i += 16) {
			Vector128<byte> v = AdvSimd.LoadVector128(src + i);
			v = AdvSimd.And(v, pl.KeepMask128);
			v = AdvSimd.Or(v, pl.FillMask128);
			Unsafe.WriteUnaligned(dst + i, v);
		}

		nuint tail = pxCount - px;
		if (tail > 0)
			ScalarKernels.Copy64SetAlpha(in plan, src + bytes, dst + bytes, tail);
	}

	public static void Shuffle32(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Shuffle32Payload pl = ref plan.Payload.Shuffle32;
		nuint px = pxCount & ~(nuint)0b11;
		nuint bytes = px * 4u;
		for (nuint i = 0; i < bytes; i += 16) {
			Vector128<byte> v = AdvSimd.LoadVector128(src + i);
			v = AdvSimd.Arm64.VectorTableLookup(v, pl.ShufMask128);
			if (pl.HasFill)
				v = AdvSimd.Or(v, pl.FillMask128);
			Unsafe.WriteUnaligned(dst + i, v);
		}

		nuint tail = pxCount - px;
		if (tail > 0)
			ScalarKernels.Shuffle32(in plan, src + bytes, dst + bytes, tail);
	}

	public static void Expand24To32(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		// 3 bytes doesn't fit nicely into an SIMD lane so this one's kind of
		// odd-looking compared to the other ones here, do a 16-byte load and
		// fish out the first 12 bytes for 4 pixels which expand to 16 output bytes

		ref readonly Expand24To32Payload pl = ref plan.Payload.Expand24To32;
		byte *s = src;
		byte *d = dst;
		nuint pixel = 0;
		// converts 4 pixels per iter, but since the load needs 16 bytes, ceil(16/3) = 6
		for (; pxCount - pixel >= 6; pixel += 4, s += 12, d += 16) {
			Vector128<byte> v = AdvSimd.LoadVector128(s);
			v = AdvSimd.Arm64.VectorTableLookup(v, pl.ShufMask128);
			v = AdvSimd.Or(v, pl.FillMask128);
			Unsafe.WriteUnaligned(d, v);
		}

		nuint tail = pxCount - pixel;
		if (tail > 0)
			ScalarKernels.Expand24To32(in plan, s, d, tail);
	}
}
