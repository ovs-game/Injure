// SPDX-License-Identifier: MIT

using System;

namespace Injure.Rendering;

[Flags]
public enum TextureUsage : ulong {
	None = 0ul,
	CopySrc = 1ul,
	CopyDst = 2ul,
	TextureBinding = 4ul,
	StorageBinding = 8ul,
	RenderAttachment = 0x10ul
}
