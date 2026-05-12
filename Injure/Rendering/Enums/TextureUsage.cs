// SPDX-License-Identifier: MIT

using System;

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedFlags]
[ClosedFlagsMirror(typeof(WebGPU.WGPUTextureUsage))]
public readonly partial struct TextureUsage {
	[Flags]
	public enum Bits : ulong {
		None = 0ul,
		CopySrc = 1ul,
		CopyDst = 2ul,
		TextureBinding = 4ul,
		StorageBinding = 8ul,
		RenderAttachment = 0x10ul,
	}
}
