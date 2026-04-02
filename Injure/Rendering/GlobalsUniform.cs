// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Injure.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct GlobalsUniform {
	public Matrix4x4 Projection;

	public static readonly int Size = Unsafe.SizeOf<GlobalsUniform>();
}
