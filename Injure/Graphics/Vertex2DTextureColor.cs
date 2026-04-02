// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Injure.Graphics;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex2DTextureColor(float x, float y, float u, float v, Color32 color) {
	public readonly float X = x;
	public readonly float Y = y;
	public readonly float U = u;
	public readonly float V = v;
	public readonly Color32 Color = color;

	public static readonly int Size = Unsafe.SizeOf<Vertex2DTextureColor>();
}
