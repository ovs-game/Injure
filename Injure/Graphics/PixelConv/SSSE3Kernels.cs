// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Injure.Graphics.PixelConv;

internal static unsafe class SSSE3Kernels {
	public static void Shuffle32(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Shuffle32Payload pl = ref plan.Payload.Shuffle32;
		nuint px = pxCount & ~(nuint)0b11;
		nuint bytes = px * 4u;
		for (nuint i = 0; i < bytes; i += 16) {
			Vector128<byte> v = Unsafe.ReadUnaligned<Vector128<byte>>(src + i);
			v = Ssse3.Shuffle(v, pl.ShufMask128);
			if (pl.HasFill)
				v = Sse2.Or(v, pl.FillMask128);
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
			Vector128<byte> v = Unsafe.ReadUnaligned<Vector128<byte>>(s);
			v = Ssse3.Shuffle(v, pl.ShufMask128);
			v = Sse2.Or(v, pl.FillMask128);
			Unsafe.WriteUnaligned(d, v);
		}

		nuint tail = pxCount - pixel;
		if (tail > 0)
			ScalarKernels.Expand24To32(in plan, s, d, tail);
	}
}
