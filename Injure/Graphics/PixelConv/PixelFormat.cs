// SPDX-License-Identifier: MIT

namespace Injure.Graphics.PixelConv;

public enum PixelFormat : ushort {
	RGBA32_UNorm,
	BGRA32_UNorm,
	ARGB32_UNorm,
	ABGR32_UNorm,

	RGBA64_UNorm_LE,
	RGBA64_UNorm_BE,
	BGRA64_UNorm_LE,
	BGRA64_UNorm_BE,
	ARGB64_UNorm_LE,
	ARGB64_UNorm_BE,
	ABGR64_UNorm_LE,
	ABGR64_UNorm_BE,

	R8_UNorm,
	RG16_UNorm,
	RGB24_UNorm,
	BGR24_UNorm,

	BGR565_UNormPack16_LE,
	BGR565_UNormPack16_BE,
	RGBA4444_UNormPack16_LE,
	RGBA4444_UNormPack16_BE,
	RGBA5551_UNormPack16_LE,
	RGBA5551_UNormPack16_BE
}

internal enum PixelFormatFamily : byte {
	ByteAligned1x8,
	ByteAligned2x8,
	ByteAligned3x8,
	ByteAligned4x8,
	ByteAligned4x16,
	Packed16
}

internal enum PixelNumericKind : byte {
	UNorm
}

internal enum PixelByteOrder : byte {
	NotApplicable,
	LittleEndian,
	BigEndian
}

internal readonly struct PixelFormatDesc(
	PixelFormatFamily family, PixelNumericKind numericKind, PixelByteOrder byteOrder, byte bytesPerPixel,
	bool hasR, bool hasG, bool hasB, bool hasA,
	byte rBits, byte gBits, byte bBits, byte aBits,
	byte rShift, byte gShift, byte bShift, byte aShift,
	sbyte rIndex, sbyte gIndex, sbyte bIndex, sbyte aIndex) {
	public readonly PixelFormatFamily Family = family;
	public readonly PixelNumericKind NumericKind = numericKind;
	public readonly PixelByteOrder ByteOrder = byteOrder;
	public readonly byte BytesPerPixel = bytesPerPixel;

	public readonly bool HasR = hasR;
	public readonly bool HasG = hasG;
	public readonly bool HasB = hasB;
	public readonly bool HasA = hasA;

	public readonly byte RBits = rBits;
	public readonly byte GBits = gBits;
	public readonly byte BBits = bBits;
	public readonly byte ABits = aBits;

	public readonly byte RShift = rShift;
	public readonly byte GShift = gShift;
	public readonly byte BShift = bShift;
	public readonly byte AShift = aShift;

	public readonly sbyte RIndex = rIndex;
	public readonly sbyte GIndex = gIndex;
	public readonly sbyte BIndex = bIndex;
	public readonly sbyte AIndex = aIndex;
}
