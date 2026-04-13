// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Injure.Graphics.PixelConv;

internal static unsafe class SSSE3Kernels {
	public static void Shuffle32(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Shuffle32Payload pl = ref plan.Payload.Shuffle32;
		nuint px = pxCount & ~0b11u;
		nuint bytes = px * 4u;
		for (nuint i = 0; i < bytes; i += 16) {
			Vector128<byte> v = Unsafe.ReadUnaligned<Vector128<byte>>(src + i);
			v = Ssse3.Shuffle(v, pl.ShufMask128);
			if (pl.HasOr)
				v = Sse2.Or(v, pl.FillMask128);
			Unsafe.WriteUnaligned(dst + i, v);
		}

		nuint tail = pxCount - px;
		if (tail > 0)
			ScalarKernels.Shuffle32(in plan, src + bytes, dst + bytes, tail);
	}
}
