// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Injure.Graphics.PixelConv;

using unsafe Kernel = delegate *<ref readonly PixelConversionPlan, byte *, byte *, nuint, void>;

internal readonly unsafe struct KernelFallbackChain(Kernel avx2, Kernel ssse3, Kernel sse2, Kernel advSIMD, Kernel scalar, bool sentinel = false) {
	public readonly Kernel AVX2 = avx2;
	public readonly Kernel SSSE3 = ssse3;
	public readonly Kernel SSE2 = sse2;
	public readonly Kernel AdvSIMD = advSIMD;
	public readonly Kernel Scalar = scalar;
	public readonly bool Sentinel = sentinel;

	public Kernel Pick(out PlanBackend chosen) {
		if (Sentinel)
			throw new InternalStateException("this KernelFallbackChain is a sentinel value");
		if (Avx2.IsSupported && AVX2 is not null) {
			chosen = PlanBackend.AVX2;
			return AVX2;
		}
		if (Ssse3.IsSupported && SSSE3 is not null) {
			chosen = PlanBackend.SSSE3;
			return SSSE3;
		}
		if (Sse2.IsSupported && SSE2 is not null) {
			chosen = PlanBackend.SSE2;
			return SSE2;
		}
		if (AdvSimd.Arm64.IsSupported && AdvSIMD is not null) {
			chosen = PlanBackend.AdvSIMD;
			return AdvSIMD;
		}
		if (Scalar is not null) {
			chosen = PlanBackend.Scalar;
			return Scalar;
		}
		throw new InternalStateException("this KernelFallbackChain is empty");
	}
}

internal static unsafe class KernelRegistry {
	public static readonly ImmutableArray<KernelFallbackChain> Kernels = [
		new KernelFallbackChain(null, null, null, null, null, sentinel: true), // ByteCopy
		new KernelFallbackChain(null, null, &SSE2Kernels.Copy32SetAlpha, null, &ScalarKernels.Copy32SetAlpha),
		new KernelFallbackChain(null, null, null, null, &ScalarKernels.Copy64SetAlpha),

		new KernelFallbackChain(&AVX2Kernels.Shuffle32, &SSSE3Kernels.Shuffle32, null, &AdvSIMDKernels.Shuffle32, &ScalarKernels.Shuffle32),
		new KernelFallbackChain(null, null, null, null, &ScalarKernels.Expand24To32),
		new KernelFallbackChain(null, null, null, null, &ScalarKernels.Contract32To24),

		new KernelFallbackChain(null, null, null, null, &ScalarKernels.Shuffle64),
		new KernelFallbackChain(null, null, null, null, &ScalarKernels.Widen32To64),
		new KernelFallbackChain(null, null, null, null, &ScalarKernels.Narrow64To32),

		new KernelFallbackChain(null, null, null, null, &ScalarKernels.Packed16To32),
		new KernelFallbackChain(null, null, null, null, &ScalarKernels.Unpacked32ToPacked16),

		new KernelFallbackChain(null, null, null, null, &ScalarKernels.Generic)
	];
}
