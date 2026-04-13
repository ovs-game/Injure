// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using static Injure.Graphics.PixelConv.ConverterCore;

namespace Injure.Graphics.PixelConv;

internal static unsafe class ScalarKernels {
	public static void Copy32SetAlpha(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Copy32SetAlphaPayload pl = ref plan.Payload.Copy32SetAlpha;
		nuint bytes = pxCount * 4;
		for (nuint i = 0; i < bytes; i += 4) {
			uint v = Unsafe.ReadUnaligned<uint>(src + i);
			Unsafe.WriteUnaligned(dst + i, v);
			dst[i + pl.AlphaByteIndex] = pl.Alpha8UNorm;
		}
	}

	public static void Copy64SetAlpha(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly Copy64SetAlphaPayload pl = ref plan.Payload.Copy64SetAlpha;
		nuint bytes = pxCount * 8;
		for (nuint i = 0; i < bytes; i += 8) {
			ulong v = Unsafe.ReadUnaligned<ulong>(src + i);
			Unsafe.WriteUnaligned(dst + i, v);
			dst[i + pl.AlphaByteOffsetInPixel] = pl.AlphaByte0;
			dst[i + pl.AlphaByteOffsetInPixel + 1] = pl.AlphaByte1;
		}
	}

	public static void Shuffle32(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly PixelFormatDesc sd = ref plan.SrcDesc;
		ref readonly PixelFormatDesc dd = ref plan.DstDesc;
		for (nuint i = 0; i < pxCount; i++) {
			byte *s = src + i * 4;
			byte *d = dst + i * 4;
			d[dd.RIndex] = s[sd.RIndex];
			d[dd.GIndex] = s[sd.GIndex];
			d[dd.BIndex] = s[sd.BIndex];
			d[dd.AIndex] = plan.Options.OverrideAlpha ? plan.Alpha8UNorm : s[sd.AIndex];
		}
	}

	public static void Expand24To32(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly PixelFormatDesc sd = ref plan.SrcDesc;
		ref readonly PixelFormatDesc dd = ref plan.DstDesc;
		for (nuint i = 0; i < pxCount; i++) {
			byte *s = src + i * 3;
			byte *d = dst + i * 4;
			d[dd.RIndex] = s[sd.RIndex];
			d[dd.GIndex] = s[sd.GIndex];
			d[dd.BIndex] = s[sd.BIndex];
			d[dd.AIndex] = plan.Alpha8UNorm;
		}
	}

	public static void Contract32To24(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly PixelFormatDesc sd = ref plan.SrcDesc;
		ref readonly PixelFormatDesc dd = ref plan.DstDesc;
		for (nuint i = 0; i < pxCount; i++) {
			byte *s = src + i * 4;
			byte *d = dst + i * 3;
			d[dd.RIndex] = s[sd.RIndex];
			d[dd.GIndex] = s[sd.GIndex];
			d[dd.BIndex] = s[sd.BIndex];
		}
	}

	public static void Shuffle64(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly PixelFormatDesc sd = ref plan.SrcDesc;
		ref readonly PixelFormatDesc dd = ref plan.DstDesc;
		for (nuint i = 0; i < pxCount; i++) {
			byte *s = src + i * 8;
			byte *d = dst + i * 8;
			WriteU16(d + dd.RIndex * 2, dd.ByteOrder, ReadU16(s + sd.RIndex * 2, sd.ByteOrder));
			WriteU16(d + dd.GIndex * 2, dd.ByteOrder, ReadU16(s + sd.GIndex * 2, sd.ByteOrder));
			WriteU16(d + dd.BIndex * 2, dd.ByteOrder, ReadU16(s + sd.BIndex * 2, sd.ByteOrder));
			WriteU16(d + dd.AIndex * 2, dd.ByteOrder, plan.Options.OverrideAlpha ? plan.Alpha16UNorm : ReadU16(s + sd.AIndex * 2, sd.ByteOrder));
		}
	}

	public static void Widen32To64(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly PixelFormatDesc sd = ref plan.SrcDesc;
		ref readonly PixelFormatDesc dd = ref plan.DstDesc;
		for (nuint i = 0; i < pxCount; i++) {
			byte *s = src + i * 4;
			byte *d = dst + i * 8;
			WriteU16(d + dd.RIndex * 2, dd.ByteOrder, Widen8To16(s[sd.RIndex]));
			WriteU16(d + dd.GIndex * 2, dd.ByteOrder, Widen8To16(s[sd.GIndex]));
			WriteU16(d + dd.BIndex * 2, dd.ByteOrder, Widen8To16(s[sd.BIndex]));
			WriteU16(d + dd.AIndex * 2, dd.ByteOrder, plan.Options.OverrideAlpha ? plan.Alpha16UNorm : Widen8To16(s[sd.AIndex]));
		}
	}

	public static void Narrow64To32(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly PixelFormatDesc sd = ref plan.SrcDesc;
		ref readonly PixelFormatDesc dd = ref plan.DstDesc;
		for (nuint i = 0; i < pxCount; i++) {
			byte *s = src + i * 8;
			byte *d = dst + i * 4;
			d[dd.RIndex] = Narrow16To8(ReadU16(s + sd.RIndex * 2, sd.ByteOrder));
			d[dd.GIndex] = Narrow16To8(ReadU16(s + sd.GIndex * 2, sd.ByteOrder));
			d[dd.BIndex] = Narrow16To8(ReadU16(s + sd.BIndex * 2, sd.ByteOrder));
			d[dd.AIndex] = plan.Options.OverrideAlpha ? plan.Alpha8UNorm : Narrow16To8(ReadU16(s + sd.AIndex * 2, sd.ByteOrder));
		}
	}

	public static void Packed16To32(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly PixelFormatDesc sd = ref plan.SrcDesc;
		ref readonly PixelFormatDesc dd = ref plan.DstDesc;
		for (nuint i = 0; i < pxCount; i++) {
			byte *s = src + i * 2;
			byte *d = dst + i * 4;
			ushort v = ReadU16(s, sd.ByteOrder);
			d[dd.RIndex] = ScaleNTo8(((uint)v >> sd.RShift) & Bitmask(sd.RBits), sd.RBits);
			d[dd.GIndex] = ScaleNTo8(((uint)v >> sd.GShift) & Bitmask(sd.GBits), sd.GBits);
			d[dd.BIndex] = ScaleNTo8(((uint)v >> sd.BShift) & Bitmask(sd.BBits), sd.BBits);
			d[dd.AIndex] = (plan.Options.OverrideAlpha || !sd.HasA) ? plan.Alpha8UNorm :
				ScaleNTo8(((uint)v >> sd.AShift) & Bitmask(sd.ABits), sd.ABits);
		}
	}

	public static void Unpacked32ToPacked16(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly PixelFormatDesc sd = ref plan.SrcDesc;
		ref readonly PixelFormatDesc dd = ref plan.DstDesc;
		for (nuint i = 0; i < pxCount; i++) {
			byte *s = src + i * 4;
			byte *d = dst + i * 2;
			uint v = 0;
			v |= Scale8ToN(s[sd.RIndex], dd.RBits) << dd.RShift;
			v |= Scale8ToN(s[sd.GIndex], dd.GBits) << dd.GShift;
			v |= Scale8ToN(s[sd.BIndex], dd.BBits) << dd.BShift;
			if (dd.HasA)
				v |= Scale8ToN(plan.Options.OverrideAlpha ? plan.Alpha8UNorm : s[sd.AIndex], dd.ABits) << dd.AShift;
			WriteU16(d, dd.ByteOrder, (ushort)v);
		}
	}

	public static void Generic(ref readonly PixelConversionPlan plan, byte *src, byte *dst, nuint pxCount) {
		ref readonly PixelFormatDesc sd = ref plan.SrcDesc;
		ref readonly PixelFormatDesc dd = ref plan.DstDesc;
		for (nuint i = 0; i < pxCount; i++) {
			byte *s = src + i * (nuint)plan.SourceBytesPerPixel;
			byte *d = dst + i * (nuint)plan.DestinationBytesPerPixel;
			DecodePixel(s, in sd, plan.Alpha16UNorm, out ushort r, out ushort g, out ushort b, out ushort a);
			if (dd.HasA && plan.Options.OverrideAlpha)
				a = plan.Alpha16UNorm;
			EncodePixel(d, in dd, r, g, b, a);
		}
	}
}
