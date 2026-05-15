// SPDX-License-Identifier: MIT

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Injure;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Color32(byte r, byte g, byte b, byte a = 255) : IEquatable<Color32> {
	public readonly byte R = r;
	public readonly byte G = g;
	public readonly byte B = b;
	public readonly byte A = a;

	public bool Equals(Color32 other) => R == other.R && G == other.G && B == other.B && A == other.A;
	public override bool Equals(object? obj) => obj is Color32 other && Equals(other);
	public override int GetHashCode() => (int)(((uint)R << 24) | ((uint)G << 16) | ((uint)B << 8) | A);
	public static bool operator ==(Color32 left, Color32 right) => left.Equals(right);
	public static bool operator !=(Color32 left, Color32 right) => !left.Equals(right);

	public override string ToString() => $"R={R} G={G} B={B} A={A}";

	public Vector4 ToVector4() => new(R / 255f, G / 255f, B / 255f, A / 255f);
	internal WebGPU.WGPUColor ToWebGPUColor() => new(R / 255.0, G / 255.0, B / 255.0, A / 255.0);
	public static Color32 FromHex(string hex) {
		static byte conv(char c) {
			c = char.ToLower(c);
			if (c >= '0' && c <= '9')
				return (byte)(c - '0');
			if (c >= 'a' && c <= 'f')
				return (byte)(c - 'a' + 0xa);
			throw new ArgumentException($"expected hex digit, got '{c}'");
		}

		int n = 0;
		if (hex.Length >= 1 && hex[0] == '#')
			n++;
		if (hex.Length - n == 6 || hex.Length - n == 8) {
			byte r = (byte)((conv(hex[n    ]) << 4) + conv(hex[n + 1]));
			byte g = (byte)((conv(hex[n + 2]) << 4) + conv(hex[n + 3]));
			byte b = (byte)((conv(hex[n + 4]) << 4) + conv(hex[n + 5]));
			byte a = 0xff;
			if (hex.Length - n == 8)
				a = (byte)((conv(hex[n + 6]) << 4) + conv(hex[n + 7]));
			return new Color32(r, g, b, a);
		}
		throw new ArgumentException("expected string of length 6 or 8 not counting optional leading # symbol");
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Color32 WithR(byte r) => new(r, G, B, A);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Color32 WithG(byte g) => new(R, g, B, A);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Color32 WithB(byte b) => new(R, G, b, A);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Color32 WithA(byte a) => new(R, G, B, a);

	public static readonly Color32 White    = new(0xff, 0xff, 0xff, 0xff);
	public static readonly Color32 Black    = new(0x00, 0x00, 0x00, 0xff);
	public static readonly Color32 Red      = new(0xff, 0x00, 0x00, 0xff);
	public static readonly Color32 Green    = new(0x00, 0xff, 0x00, 0xff);
	public static readonly Color32 Blue     = new(0x00, 0x00, 0xff, 0xff);
	public static readonly Color32 Yellow   = new(0xff, 0xff, 0x00, 0xff);
	public static readonly Color32 Cyan     = new(0x00, 0xff, 0xff, 0xff);
	public static readonly Color32 Magenta  = new(0xff, 0x00, 0xff, 0xff);

	public static readonly Color32 DarkBlue = new(0x00, 0x00, 0x8b, 0xff);
}
