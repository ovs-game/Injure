// SPDX-License-Identifier: MIT

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Injure.Graphics.PixelConv;

internal static unsafe class ConverterCore {
	public static readonly FrozenDictionary<PixelFormat, PixelFormatDesc> FormatDescs = new Dictionary<PixelFormat, PixelFormatDesc>() {
		[PixelFormat.RGBA32_UNorm] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned4x8, PixelNumericKind.UNorm, PixelByteOrder.NotApplicable, bytesPerPixel: 4,
			true, true, true, true,
			8, 8, 8, 8,
			0, 0, 0, 0,
			0, 1, 2, 3
		),
		[PixelFormat.BGRA32_UNorm] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned4x8, PixelNumericKind.UNorm, PixelByteOrder.NotApplicable, bytesPerPixel: 4,
			true, true, true, true,
			8, 8, 8, 8,
			0, 0, 0, 0,
			2, 1, 0, 3
		),
		[PixelFormat.ARGB32_UNorm] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned4x8, PixelNumericKind.UNorm, PixelByteOrder.NotApplicable, bytesPerPixel: 4,
			true, true, true, true,
			8, 8, 8, 8,
			0, 0, 0, 0,
			1, 2, 3, 0
		),
		[PixelFormat.ABGR32_UNorm] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned4x8, PixelNumericKind.UNorm, PixelByteOrder.NotApplicable, bytesPerPixel: 4,
			true, true, true, true,
			8, 8, 8, 8,
			0, 0, 0, 0,
			3, 2, 1, 0
		),
		[PixelFormat.RGBA64_UNorm_LE] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned4x16, PixelNumericKind.UNorm, PixelByteOrder.LittleEndian, bytesPerPixel: 8,
			true, true, true, true,
			16, 16, 16, 16,
			0, 0, 0, 0,
			0, 1, 2, 3
		),
		[PixelFormat.RGBA64_UNorm_BE] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned4x16, PixelNumericKind.UNorm, PixelByteOrder.BigEndian, bytesPerPixel: 8,
			true, true, true, true,
			16, 16, 16, 16,
			0, 0, 0, 0,
			0, 1, 2, 3
		),
		[PixelFormat.BGRA64_UNorm_LE] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned4x16, PixelNumericKind.UNorm, PixelByteOrder.LittleEndian, bytesPerPixel: 8,
			true, true, true, true,
			16, 16, 16, 16,
			0, 0, 0, 0,
			2, 1, 0, 3
		),
		[PixelFormat.BGRA64_UNorm_BE] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned4x16, PixelNumericKind.UNorm, PixelByteOrder.BigEndian, bytesPerPixel: 8,
			true, true, true, true,
			16, 16, 16, 16,
			0, 0, 0, 0,
			2, 1, 0, 3
		),
		[PixelFormat.ARGB64_UNorm_LE] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned4x16, PixelNumericKind.UNorm, PixelByteOrder.LittleEndian, bytesPerPixel: 8,
			true, true, true, true,
			16, 16, 16, 16,
			0, 0, 0, 0,
			1, 2, 3, 0
		),
		[PixelFormat.ARGB64_UNorm_BE] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned4x16, PixelNumericKind.UNorm, PixelByteOrder.BigEndian, bytesPerPixel: 8,
			true, true, true, true,
			16, 16, 16, 16,
			0, 0, 0, 0,
			1, 2, 3, 0
		),
		[PixelFormat.ABGR64_UNorm_LE] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned4x16, PixelNumericKind.UNorm, PixelByteOrder.LittleEndian, bytesPerPixel: 8,
			true, true, true, true,
			16, 16, 16, 16,
			0, 0, 0, 0,
			3, 2, 1, 0
		),
		[PixelFormat.ABGR64_UNorm_BE] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned4x16, PixelNumericKind.UNorm, PixelByteOrder.BigEndian, bytesPerPixel: 8,
			true, true, true, true,
			16, 16, 16, 16,
			0, 0, 0, 0,
			3, 2, 1, 0
		),
		[PixelFormat.R8_UNorm] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned1x8, PixelNumericKind.UNorm, PixelByteOrder.NotApplicable, bytesPerPixel: 1,
			true, false, false, false,
			8, 0, 0, 0,
			0, 0, 0, 0,
			0, -1, -1, -1
		),
		[PixelFormat.RG16_UNorm] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned2x8, PixelNumericKind.UNorm, PixelByteOrder.NotApplicable, bytesPerPixel: 2,
			true, true, false, false,
			8, 8, 0, 0,
			0, 0, 0, 0,
			0, 1, -1, -1
		),
		[PixelFormat.RGB24_UNorm] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned3x8, PixelNumericKind.UNorm, PixelByteOrder.NotApplicable, bytesPerPixel: 3,
			true, true, true, false,
			8, 8, 8, 0,
			0, 0, 0, 0,
			0, 1, 2, -1
		),
		[PixelFormat.BGR24_UNorm] = new PixelFormatDesc(
			PixelFormatFamily.ByteAligned3x8, PixelNumericKind.UNorm, PixelByteOrder.NotApplicable, bytesPerPixel: 3,
			true, true, true, false,
			8, 8, 8, 0,
			0, 0, 0, 0,
			2, 1, 0, -1
		),
		[PixelFormat.BGR565_UNormPack16_LE] = new PixelFormatDesc(
			PixelFormatFamily.Packed16, PixelNumericKind.UNorm, PixelByteOrder.LittleEndian, bytesPerPixel: 2,
			true, true, true, false,
			5, 6, 5, 0,
			11, 5, 0, 0,
			-1, -1, -1, -1
		),
		[PixelFormat.BGR565_UNormPack16_BE] = new PixelFormatDesc(
			PixelFormatFamily.Packed16, PixelNumericKind.UNorm, PixelByteOrder.BigEndian, bytesPerPixel: 2,
			true, true, true, false,
			5, 6, 5, 0,
			11, 5, 0, 0,
			-1, -1, -1, -1
		),
		[PixelFormat.RGBA4444_UNormPack16_LE] = new PixelFormatDesc(
			PixelFormatFamily.Packed16, PixelNumericKind.UNorm, PixelByteOrder.LittleEndian, bytesPerPixel: 2,
			true, true, true, true,
			4, 4, 4, 4,
			12, 8, 4, 0,
			-1, -1, -1, -1
		),
		[PixelFormat.RGBA4444_UNormPack16_BE] = new PixelFormatDesc(
			PixelFormatFamily.Packed16, PixelNumericKind.UNorm, PixelByteOrder.BigEndian, bytesPerPixel: 2,
			true, true, true, true,
			4, 4, 4, 4,
			12, 8, 4, 0,
			-1, -1, -1, -1
		),
		[PixelFormat.RGBA5551_UNormPack16_LE] = new PixelFormatDesc(
			PixelFormatFamily.Packed16, PixelNumericKind.UNorm, PixelByteOrder.LittleEndian, bytesPerPixel: 2,
			true, true, true, true,
			5, 5, 5, 1,
			11, 6, 1, 0,
			-1, -1, -1, -1
		),
		[PixelFormat.RGBA5551_UNormPack16_BE] = new PixelFormatDesc(
			PixelFormatFamily.Packed16, PixelNumericKind.UNorm, PixelByteOrder.BigEndian, bytesPerPixel: 2,
			true, true, true, true,
			5, 5, 5, 1,
			11, 6, 1, 0,
			-1, -1, -1, -1
		),
	}.ToFrozenDictionary();

	public static PlanKind Classify(PixelFormat srcFmt, PixelFormat dstFmt, in PixelFormatDesc srcDesc, in PixelFormatDesc dstDesc, in PixelConvertOptions opts) {
		if (srcFmt == dstFmt) {
			if (!dstDesc.HasA || !opts.OverrideAlpha)
				return PlanKind.ByteCopy;
			if (dstDesc.Family == PixelFormatFamily.ByteAligned4x8)
				return PlanKind.Copy32SetAlpha;
			if (dstDesc.Family == PixelFormatFamily.ByteAligned4x16)
				return PlanKind.Copy64SetAlpha;
		}
		if (srcDesc.Family == PixelFormatFamily.ByteAligned4x8 && dstDesc.Family == PixelFormatFamily.ByteAligned4x8)
			return PlanKind.Shuffle32;
		if (srcDesc.Family == PixelFormatFamily.ByteAligned3x8 && dstDesc.Family == PixelFormatFamily.ByteAligned4x8)
			return PlanKind.Expand24To32;
		if (srcDesc.Family == PixelFormatFamily.ByteAligned4x8 && dstDesc.Family == PixelFormatFamily.ByteAligned3x8)
			return PlanKind.Contract32To24;
		if (srcDesc.Family == PixelFormatFamily.ByteAligned4x16 && dstDesc.Family == PixelFormatFamily.ByteAligned4x16)
			return PlanKind.Shuffle64;
		if (srcDesc.Family == PixelFormatFamily.ByteAligned4x8 && dstDesc.Family == PixelFormatFamily.ByteAligned4x16)
			return PlanKind.Widen32To64;
		if (srcDesc.Family == PixelFormatFamily.ByteAligned4x16 && dstDesc.Family == PixelFormatFamily.ByteAligned4x8)
			return PlanKind.Narrow64To32;
		if (srcDesc.Family == PixelFormatFamily.Packed16 && dstDesc.Family == PixelFormatFamily.ByteAligned4x8)
			return PlanKind.Packed16To32;
		if (srcDesc.Family == PixelFormatFamily.ByteAligned4x8 && dstDesc.Family == PixelFormatFamily.Packed16)
			return PlanKind.Unpacked32ToPacked16;
		return PlanKind.Generic;
	}

	public static bool WouldNarrow(in PixelFormatDesc src, in PixelFormatDesc dst) {
		static bool check(bool haveSrc, bool haveDst, byte srcBits, byte dstBits) =>
			haveSrc && haveDst && srcBits != 0 && dstBits != 0 && dstBits < srcBits;
		if (check(src.HasR, dst.HasR, src.RBits, dst.RBits)) return true;
		if (check(src.HasG, dst.HasG, src.GBits, dst.GBits)) return true;
		if (check(src.HasB, dst.HasB, src.BBits, dst.BBits)) return true;
		if (check(src.HasA, dst.HasA, src.ABits, dst.ABits)) return true;
		return false;
	}
	public static bool WouldDropAlpha(in PixelFormatDesc src, in PixelFormatDesc dst) => src.HasA && !dst.HasA;
	public static bool WouldDropColorChannels(in PixelFormatDesc src, in PixelFormatDesc dst) =>
		(src.HasR && !dst.HasR) || (src.HasG && !dst.HasG) || (src.HasB && !dst.HasB);

	public static PayloadUnion MakePayload(PlanKind kind, in PixelFormatDesc src, in PixelFormatDesc dst, in PixelConvertOptions opts) {
		PayloadUnion payload = default;
		switch (kind) {
		case PlanKind.Copy32SetAlpha:
			payload.Copy32SetAlpha = MakeCopy32SetAlphaPayload(in dst, in opts);
			break;
		case PlanKind.Copy64SetAlpha:
			payload.Copy64SetAlpha = MakeCopy64SetAlphaPayload(in dst, in opts);
			break;
		case PlanKind.Shuffle32:
			payload.Shuffle32 = MakeShuffle32Payload(in src, in dst, in opts);
			break;
		}
		return payload;
	}

	public static Copy32SetAlphaPayload MakeCopy32SetAlphaPayload(in PixelFormatDesc dst, in PixelConvertOptions opts) {
		byte byteIndex = checked((byte)dst.AIndex);
		byte a8 = Narrow16To8(opts.Alpha16UNorm);
		byte *keep = stackalloc byte[16];
		byte *fill = stackalloc byte[16];
		Unsafe.InitBlockUnaligned(fill, 0xff, 16);
		for (int i = 0; i < 16; i += 4) {
			keep[i + byteIndex] = 0x00;
			fill[i + byteIndex] = a8;
		}
		Vector128<byte> keep128 = Unsafe.ReadUnaligned<Vector128<byte>>(keep);
		Vector128<byte> fill128 = Unsafe.ReadUnaligned<Vector128<byte>>(fill);
		return new Copy32SetAlphaPayload(byteIndex, a8, keep128, fill128);
	}

	public static Copy64SetAlphaPayload MakeCopy64SetAlphaPayload(in PixelFormatDesc dst, in PixelConvertOptions opts) {
		byte byteOffsetInPixel = checked((byte)(dst.AIndex * 2));
		ushort a16 = opts.Alpha16UNorm;
		byte byte0;
		byte byte1;
		switch (dst.ByteOrder) {
			case PixelByteOrder.LittleEndian:
				byte0 = (byte)a16;
				byte1 = (byte)(a16 >> 8);
				break;
			case PixelByteOrder.BigEndian:
				byte0 = (byte)(a16 >> 8);
				byte1 = (byte)a16;
				break;
			default:
				throw new UnreachableException();
		}
		byte *keep = stackalloc byte[16];
		byte *fill = stackalloc byte[16];
		Unsafe.InitBlockUnaligned(fill, 0xff, 16);
		for (int i = 0; i < 16; i += 8) {
			keep[i + byteOffsetInPixel] = 0x00;
			keep[i + byteOffsetInPixel + 1] = 0x00;
			fill[i + byteOffsetInPixel] = byte0;
			fill[i + byteOffsetInPixel + 1] = byte1;
		}
		Vector128<byte> keep128 = Unsafe.ReadUnaligned<Vector128<byte>>(keep);
		Vector128<byte> fill128 = Unsafe.ReadUnaligned<Vector128<byte>>(fill);
		return new Copy64SetAlphaPayload(byteOffsetInPixel, byte0, byte1, a16, keep128, fill128);
	}

	public static Shuffle32Payload MakeShuffle32Payload(in PixelFormatDesc src, in PixelFormatDesc dst, in PixelConvertOptions opts) {
		bool hasFill = false;
		byte a8 = Narrow16To8(opts.Alpha16UNorm);
		byte *shuf = stackalloc byte[16];
		byte *fill = stackalloc byte[16];
		for (int i = 0; i < 16; i += 4) {
			shuf[i + dst.RIndex] = (byte)(i + src.RIndex);
			shuf[i + dst.GIndex] = (byte)(i + src.GIndex);
			shuf[i + dst.BIndex] = (byte)(i + src.BIndex);
			if (opts.OverrideAlpha) {
				shuf[i + dst.AIndex] = 1 << 7;
				fill[i + dst.AIndex] = a8;
				hasFill = true;
			} else {
				shuf[i + dst.AIndex] = (byte)(i + src.AIndex);
			}
		}
		Vector128<byte> shuf128 = Unsafe.ReadUnaligned<Vector128<byte>>(shuf);
		Vector128<byte> fill128 = Unsafe.ReadUnaligned<Vector128<byte>>(fill);
		return new Shuffle32Payload(shuf128, fill128, hasFill);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ushort Widen8To16(byte b) => (ushort)((b << 8) | b);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte Narrow16To8(ushort s) => (byte)((s * 0xffu + 0x7fffu) / 0xffffu);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static uint Bitmask(int bits) {
		if (bits <= 0)
			return 0;
		if (bits >= 32)
			return uint.MaxValue;
		return (1u << bits) - 1u;
	}

	public static byte ScaleNTo8(uint val, int bits) {
		if (bits <= 0)
			return 0;
		if (bits == 8)
			return (byte)val;
		uint max = Bitmask(bits);
		return (byte)((val * 0xffu + (max >> 1)) / max);
	}

	public static uint Scale8ToN(byte val, int bits) {
		if (bits <= 0)
			return 0;
		if (bits == 8)
			return val;
		uint max = Bitmask(bits);
		return (val * max + 0x7fu) / 0xffu;
	}

	public static ushort ScaleNTo16(uint val, int bits) {
		if (bits <= 0)
			return 0;
		if (bits == 16)
			return (ushort)val;
		uint max = Bitmask(bits);
		return (ushort)((val * 0xffffu + (max >> 1)) / max);
	}

	public static uint Scale16ToN(ushort val, int bits) {
		if (bits <= 0)
			return 0;
		if (bits == 16)
			return val;
		uint max = Bitmask(bits);
		return (val * max + 0x7fffu) / 0xffffu;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ushort ReadU16(byte *p, PixelByteOrder order) {
		return order switch {
			PixelByteOrder.LittleEndian => (ushort)(p[0] | (p[1] << 8)),
			PixelByteOrder.BigEndian => (ushort)(p[1] | (p[0] << 8)),
			PixelByteOrder.NotApplicable => throw new InternalStateException("NotApplicable byte order passed to ReadU16"),
			_ => throw new UnreachableException(),
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void WriteU16(byte *p, PixelByteOrder order, ushort val) {
		switch (order) {
		case PixelByteOrder.LittleEndian:
			p[0] = (byte)val;
			p[1] = (byte)(val >> 8);
			return;
		case PixelByteOrder.BigEndian:
			p[0] = (byte)(val >> 8);
			p[1] = (byte)val;
			return;
		case PixelByteOrder.NotApplicable:
			throw new InternalStateException("NotApplicable byte order passed to WriteU16");
		default:
			throw new UnreachableException();
		}
	}

	public static void DecodePixel(byte *src, in PixelFormatDesc desc, ushort defaultA, out ushort r, out ushort g, out ushort b, out ushort a) {
		r = g = b = 0;
		a = defaultA;
		switch (desc.Family) {
		case PixelFormatFamily.ByteAligned1x8:
			r = Widen8To16(src[desc.RIndex]);
			return;
		case PixelFormatFamily.ByteAligned2x8:
			r = Widen8To16(src[desc.RIndex]);
			g = Widen8To16(src[desc.GIndex]);
			return;
		case PixelFormatFamily.ByteAligned3x8:
			r = Widen8To16(src[desc.RIndex]);
			g = Widen8To16(src[desc.GIndex]);
			b = Widen8To16(src[desc.BIndex]);
			return;
		case PixelFormatFamily.ByteAligned4x8:
			r = Widen8To16(src[desc.RIndex]);
			g = Widen8To16(src[desc.GIndex]);
			b = Widen8To16(src[desc.BIndex]);
			a = Widen8To16(src[desc.AIndex]);
			return;
		case PixelFormatFamily.ByteAligned4x16:
			r = ReadU16(src + desc.RIndex * 2, desc.ByteOrder);
			g = ReadU16(src + desc.GIndex * 2, desc.ByteOrder);
			b = ReadU16(src + desc.BIndex * 2, desc.ByteOrder);
			a = ReadU16(src + desc.AIndex * 2, desc.ByteOrder);
			return;
		case PixelFormatFamily.Packed16:
			ushort v = ReadU16(src, desc.ByteOrder);
			r = ScaleNTo16(((uint)v >> desc.RShift) & Bitmask(desc.RBits), desc.RBits);
			g = ScaleNTo16(((uint)v >> desc.GShift) & Bitmask(desc.GBits), desc.GBits);
			b = ScaleNTo16(((uint)v >> desc.BShift) & Bitmask(desc.BBits), desc.BBits);
			if (desc.HasA)
				a = ScaleNTo16(((uint)v >> desc.AShift) & Bitmask(desc.ABits), desc.ABits);
			return;
		default:
			throw new InternalStateException("DecodePixel doesn't have a case for this format");
		}
	}

	public static void EncodePixel(byte *dst, in PixelFormatDesc desc, ushort r, ushort g, ushort b, ushort a) {
		switch (desc.Family) {
		case PixelFormatFamily.ByteAligned1x8:
			dst[desc.RIndex] = Narrow16To8(r);
			return;
		case PixelFormatFamily.ByteAligned2x8:
			dst[desc.RIndex] = Narrow16To8(r);
			dst[desc.GIndex] = Narrow16To8(g);
			return;
		case PixelFormatFamily.ByteAligned3x8:
			dst[desc.RIndex] = Narrow16To8(r);
			dst[desc.GIndex] = Narrow16To8(g);
			dst[desc.BIndex] = Narrow16To8(b);
			return;
		case PixelFormatFamily.ByteAligned4x8:
			dst[desc.RIndex] = Narrow16To8(r);
			dst[desc.GIndex] = Narrow16To8(g);
			dst[desc.BIndex] = Narrow16To8(b);
			dst[desc.AIndex] = Narrow16To8(a);
			return;
		case PixelFormatFamily.ByteAligned4x16:
			WriteU16(dst + desc.RIndex * 2, desc.ByteOrder, r);
			WriteU16(dst + desc.GIndex * 2, desc.ByteOrder, g);
			WriteU16(dst + desc.BIndex * 2, desc.ByteOrder, b);
			WriteU16(dst + desc.AIndex * 2, desc.ByteOrder, a);
			return;
		case PixelFormatFamily.Packed16:
			uint v = 0;
			v |= Scale16ToN(r, desc.RBits) << desc.RShift;
			v |= Scale16ToN(r, desc.GBits) << desc.GShift;
			v |= Scale16ToN(r, desc.BBits) << desc.BShift;
			if (desc.HasA)
				v |= Scale16ToN(r, desc.ABits) << desc.AShift;
			WriteU16(dst, desc.ByteOrder, (ushort)v);
			return;
		default:
			throw new InternalStateException("EncodePixel doesn't have a case for this format");
		}
	}
}
