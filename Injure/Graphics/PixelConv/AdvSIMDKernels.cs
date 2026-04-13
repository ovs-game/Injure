// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

namespace Injure.Graphics.PixelConv;

internal static unsafe class AdvSIMDKernels {
	public static void Shuffle32(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Shuffle32Payload pl = ref plan.Payload.Shuffle32;
		nuint px = pxCount & ~0b11u;
		nuint bytes = px * 4u;
		for (nuint i = 0; i < bytes; i += 16) {
			Vector128<byte> v = Unsafe.ReadUnaligned<Vector128<byte>>(src + i);
			v = AdvSimd.Arm64.VectorTableLookup(v, pl.ShufMask128);
			if (pl.HasFill)
				v = AdvSimd.Or(v, pl.FillMask128);
			Unsafe.WriteUnaligned(dst + i, v);
		}

		nuint tail = pxCount - px;
		if (tail > 0)
			ScalarKernels.Shuffle32(in plan, src + bytes, dst + bytes, tail);
	}
}
