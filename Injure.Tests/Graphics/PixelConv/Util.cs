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

	public static int GetBytesPerPixel(PixelFormat fmt) => fmt switch {
		PixelFormat.RGBA32_UNorm => 4,
		PixelFormat.BGRA32_UNorm => 4,
		PixelFormat.ARGB32_UNorm => 4,
		PixelFormat.ABGR32_UNorm => 4,
		PixelFormat.RGBA64_UNorm_LE => 8,
		PixelFormat.RGBA64_UNorm_BE => 8,
		PixelFormat.BGRA64_UNorm_LE => 8,
		PixelFormat.BGRA64_UNorm_BE => 8,
		PixelFormat.ARGB64_UNorm_LE => 8,
		PixelFormat.ARGB64_UNorm_BE => 8,
		PixelFormat.ABGR64_UNorm_LE => 8,
		PixelFormat.ABGR64_UNorm_BE => 8,
		PixelFormat.R8_UNorm => 1,
		PixelFormat.RG16_UNorm => 2,
		PixelFormat.RGB24_UNorm => 3,
		PixelFormat.BGR24_UNorm => 3,
		PixelFormat.BGR565_UNormPack16_LE => 2,
		PixelFormat.BGR565_UNormPack16_BE => 2,
		PixelFormat.RGBA4444_UNormPack16_LE => 2,
		PixelFormat.RGBA4444_UNormPack16_BE => 2,
		PixelFormat.RGBA5551_UNormPack16_LE => 2,
		PixelFormat.RGBA5551_UNormPack16_BE => 2,
		_ => throw new UnreachableException()
	};

}
