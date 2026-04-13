// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Injure.Graphics.PixelConv;

internal readonly struct Copy32SetAlphaPayload(byte byteIndex, byte a8unorm, Vector128<byte> keep128, Vector128<byte> fill128) {
	public readonly byte AlphaByteIndex = byteIndex;
	public readonly byte Alpha8UNorm = a8unorm;

	public readonly Vector128<byte> KeepMask128 = keep128;
	public readonly Vector128<byte> FillMask128 = fill128;
	public readonly Vector256<byte> KeepMask256 = Vector256.Create(keep128, keep128);
	public readonly Vector256<byte> FillMask256 = Vector256.Create(fill128, fill128);
}

internal readonly struct Copy64SetAlphaPayload(byte byteOffsetInPixel, byte byte0, byte byte1, ushort a16unorm, Vector128<byte> keep128, Vector128<byte> fill128) {
	public readonly byte AlphaByteOffsetInPixel = byteOffsetInPixel;
	public readonly byte AlphaByte0 = byte0;
	public readonly byte AlphaByte1 = byte1;
	public readonly ushort Alpha16UNorm = a16unorm;

	public readonly Vector128<byte> KeepMask128 = keep128;
	public readonly Vector128<byte> FillMask128 = fill128;
	public readonly Vector256<byte> KeepMask256 = Vector256.Create(keep128, keep128);
	public readonly Vector256<byte> FillMask256 = Vector256.Create(fill128, fill128);
}

internal readonly struct Shuffle32Payload(Vector128<byte> shuf128, Vector128<byte> fill128, bool hasOr) {
	public readonly Vector128<byte> ShufMask128 = shuf128;
	public readonly Vector128<byte> FillMask128 = fill128;
	public readonly Vector256<byte> ShufMask256 = Vector256.Create(shuf128, shuf128);
	public readonly Vector256<byte> FillMask256 = Vector256.Create(fill128, fill128);
	public readonly bool HasOr = hasOr;
}

[StructLayout(LayoutKind.Explicit)]
internal struct PayloadUnion {
	[FieldOffset(0)] public Copy32SetAlphaPayload Copy32SetAlpha;
	[FieldOffset(0)] public Copy64SetAlphaPayload Copy64SetAlpha;
	[FieldOffset(0)] public Shuffle32Payload Shuffle32;
}
