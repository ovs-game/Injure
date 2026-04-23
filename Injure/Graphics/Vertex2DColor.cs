// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Injure.Graphics;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex2DColor(float x, float y, Color32 color) {
	public readonly float X = x;
	public readonly float Y = y;
	public readonly Color32 Color = color;

	public static readonly int Size = Unsafe.SizeOf<Vertex2DColor>();

	public Vertex2DColor(Vector2 xy, Color32 color) : this(xy.X, xy.Y, color) {}
	public static explicit operator Vector2(Vertex2DColor v) => new(v.X, v.Y);
}
