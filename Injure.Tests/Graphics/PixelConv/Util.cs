// SPDX-License-Identifier: MIT

using System.Diagnostics;

using Injure.Graphics.PixelConv;

namespace Injure.Tests.Graphics.PixelConv;

public static class Util {
	public static byte[] Rgba64LE(params (ushort R, ushort G, ushort B, ushort A)[] pixels) {
		static void writeU16LE(byte[] dst, int offset, ushort val) {
			dst[offset + 0] = (byte)val;
			dst[offset + 1] = (byte)(val >> 8);
		}

		byte[] result = new byte[pixels.Length * 8];
		for (int i = 0; i < pixels.Length; i++) {
			int b = i * 8;
			writeU16LE(result, b + 0, pixels[i].R);
			writeU16LE(result, b + 2, pixels[i].G);
			writeU16LE(result, b + 4, pixels[i].B);
			writeU16LE(result, b + 6, pixels[i].A);
		}
		return result;
	}

	public static int GetBytesPerPixel(PixelFormat fmt) => fmt.Tag switch {
		PixelFormat.Case.RGBA32_UNorm => 4,
		PixelFormat.Case.BGRA32_UNorm => 4,
		PixelFormat.Case.ARGB32_UNorm => 4,
		PixelFormat.Case.ABGR32_UNorm => 4,
		PixelFormat.Case.RGBA64_UNorm_LE => 8,
		PixelFormat.Case.RGBA64_UNorm_BE => 8,
		PixelFormat.Case.BGRA64_UNorm_LE => 8,
		PixelFormat.Case.BGRA64_UNorm_BE => 8,
		PixelFormat.Case.ARGB64_UNorm_LE => 8,
		PixelFormat.Case.ARGB64_UNorm_BE => 8,
		PixelFormat.Case.ABGR64_UNorm_LE => 8,
		PixelFormat.Case.ABGR64_UNorm_BE => 8,
		PixelFormat.Case.R8_UNorm => 1,
		PixelFormat.Case.RG16_UNorm => 2,
		PixelFormat.Case.RGB24_UNorm => 3,
		PixelFormat.Case.BGR24_UNorm => 3,
		PixelFormat.Case.BGR565_UNormPack16_LE => 2,
		PixelFormat.Case.BGR565_UNormPack16_BE => 2,
		PixelFormat.Case.RGBA4444_UNormPack16_LE => 2,
		PixelFormat.Case.RGBA4444_UNormPack16_BE => 2,
		PixelFormat.Case.RGBA5551_UNormPack16_LE => 2,
		PixelFormat.Case.RGBA5551_UNormPack16_BE => 2,
		_ => throw new UnreachableException()
	};

}
