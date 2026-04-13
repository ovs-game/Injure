// SPDX-License-Identifier: MIT

namespace Injure.Tests.Graphics.PixelConv;

public static class Util {
	public static byte[] Rgba64LE(params (ushort R, ushort G, ushort B, ushort A)[] pixels) {
		byte[] result = new byte[pixels.Length * 8];
		for (int i = 0; i < pixels.Length; i++) {
			int b = i * 8;
			WriteU16LE(result, b + 0, pixels[i].R);
			WriteU16LE(result, b + 2, pixels[i].G);
			WriteU16LE(result, b + 4, pixels[i].B);
			WriteU16LE(result, b + 6, pixels[i].A);
		}
		return result;
	}

	private static void WriteU16LE(byte[] dst, int offset, ushort val) {
		dst[offset + 0] = (byte)val;
		dst[offset + 1] = (byte)(val >> 8);
	}
}
