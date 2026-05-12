// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Graphics.PixelConv;

/// <summary>
/// Identifies a pixel storage format.
/// </summary>
/// <remarks>
/// <para>
/// Unless explicitly otherwise noted, pixels are tightly packed with no padding.
/// For example, four RGB24 pixels occupy 12 bytes, not e.g 16.
/// </para>
/// <para>
/// Only storage is described; higher-level semantics such as premultiplied alpha,
/// transfer function, or colorspace are not encoded.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct PixelFormat {
	/// <summary>Raw switch tag for <see cref="PixelFormat"/>.</summary>
	public enum Case {
		/// <summary>
		/// RGBA stored as four 8-bit unsigned normalized channels.
		/// </summary>
		RGBA32_UNorm = 1,

		/// <summary>
		/// BGRA stored as four 8-bit unsigned normalized channels.
		/// </summary>
		BGRA32_UNorm,

		/// <summary>
		/// ARGB stored as four 8-bit unsigned normalized channels.
		/// </summary>
		ARGB32_UNorm,

		/// <summary>
		/// ABGR stored as four 8-bit unsigned normalized channels.
		/// </summary>
		ABGR32_UNorm,

		/// <summary>
		/// RGBA stored as four 16-bit unsigned normalized channels in little-endian channel byte order.
		/// </summary>
		RGBA64_UNorm_LE,

		/// <summary>
		/// RGBA stored as four 16-bit unsigned normalized channels in big-endian channel byte order.
		/// </summary>
		RGBA64_UNorm_BE,

		/// <summary>
		/// BGRA stored as four 16-bit unsigned normalized channels in little-endian channel byte order.
		/// </summary>
		BGRA64_UNorm_LE,

		/// <summary>
		/// BGRA stored as four 16-bit unsigned normalized channels in big-endian channel byte order.
		/// </summary>
		BGRA64_UNorm_BE,

		/// <summary>
		/// ARGB stored as four 16-bit unsigned normalized channels in little-endian channel byte order.
		/// </summary>
		ARGB64_UNorm_LE,

		/// <summary>
		/// ARGB stored as four 16-bit unsigned normalized channels in big-endian channel byte order.
		/// </summary>
		ARGB64_UNorm_BE,

		/// <summary>
		/// ABGR stored as four 16-bit unsigned normalized channels in little-endian channel byte order.
		/// </summary>
		ABGR64_UNorm_LE,

		/// <summary>
		/// ABGR stored as four 16-bit unsigned normalized channels in big-endian channel byte order.
		/// </summary>
		ABGR64_UNorm_BE,

		/// <summary>
		/// A single 8-bit unsigned normalized red channel.
		/// </summary>
		R8_UNorm,

		/// <summary>
		/// Red + green stored as two 8-bit unsigned normalized channels.
		/// </summary>
		RG16_UNorm,

		/// <summary>
		/// RGB stored as three 8-bit unsigned normalized channels.
		/// </summary>
		RGB24_UNorm,

		/// <summary>
		/// BGR stored as three 8-bit unsigned normalized channels.
		/// </summary>
		BGR24_UNorm,

		/// <summary>
		/// BGR stored as three 5:6:5 unsigned normalized channels packed into
		/// 16 bits in little-endian byte order.
		/// </summary>
		BGR565_UNormPack16_LE,

		/// <summary>
		/// BGR stored as three 5:6:5 unsigned normalized channels packed into
		/// 16 bits in big-endian byte order.
		/// </summary>
		BGR565_UNormPack16_BE,

		/// <summary>
		/// RGBA stored as four 4-bit unsigned normalized channels packed into
		/// 16 bits in little-endian byte order.
		/// </summary>
		RGBA4444_UNormPack16_LE,

		/// <summary>
		/// RGBA stored as four 4-bit unsigned normalized channels packed into
		/// 16 bits in big-endian byte order.
		/// </summary>
		RGBA4444_UNormPack16_BE,

		/// <summary>
		/// RGBA stored as four 5:5:5:1 unsigned normalized channels packed into
		/// 16 bits in little-endian byte order.
		/// </summary>
		RGBA5551_UNormPack16_LE,

		/// <summary>
		/// RGBA stored as four 5:5:5:1 unsigned normalized channels packed into
		/// 16 bits in big-endian byte order.
		/// </summary>
		RGBA5551_UNormPack16_BE,
	}
}

internal enum PixelFormatFamily : byte {
	ByteAligned1x8,
	ByteAligned2x8,
	ByteAligned3x8,
	ByteAligned4x8,
	ByteAligned4x16,
	Packed16,
}

internal enum PixelNumericKind : byte {
	UNorm,
}

internal enum PixelByteOrder : byte {
	NotApplicable,
	LittleEndian,
	BigEndian,
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
