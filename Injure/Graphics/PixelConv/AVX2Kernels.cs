// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Injure.Graphics.PixelConv;

internal static unsafe class AVX2Kernels {
	public static void Shuffle32(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Shuffle32Payload pl = ref plan.Payload.Shuffle32;
		nuint px = pxCount & ~0b111u;
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
}
