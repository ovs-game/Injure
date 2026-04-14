// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Injure.Graphics.PixelConv;

internal static unsafe class SSE2Kernels {
	public static void Copy32SetAlpha(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Copy32SetAlphaPayload pl = ref plan.Payload.Copy32SetAlpha;
		nuint px = pxCount & ~(nuint)0b11;
		nuint bytes = px * 4u;
		for (nuint i = 0; i < bytes; i += 16) {
			Vector128<byte> v = Unsafe.ReadUnaligned<Vector128<byte>>(src + i);
			v = Sse2.And(v, pl.KeepMask128);
			v = Sse2.Or(v, pl.FillMask128);
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
			Vector128<byte> v = Unsafe.ReadUnaligned<Vector128<byte>>(src + i);
			v = Sse2.And(v, pl.KeepMask128);
			v = Sse2.Or(v, pl.FillMask128);
			Unsafe.WriteUnaligned(dst + i, v);
		}

		nuint tail = pxCount - px;
		if (tail > 0)
			ScalarKernels.Copy64SetAlpha(in plan, src + bytes, dst + bytes, tail);
	}
}
