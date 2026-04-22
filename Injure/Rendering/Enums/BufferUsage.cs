// SPDX-License-Identifier: MIT

using System;

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedFlags]
[ClosedFlagsMirror(typeof(WebGPU.WGPUBufferUsage))]
public readonly partial struct BufferUsage {
	[Flags]
	public enum Bits : ulong {
		None = 0ul,
		MapRead = 1ul,
		MapWrite = 2ul,
		CopySrc = 4ul,
		CopyDst = 8ul,
		Index = 0x10ul,
		Vertex = 0x20ul,
		Uniform = 0x40ul,
		Storage = 0x80ul,
		Indirect = 0x100ul,
		QueryResolve = 0x200ul
	}
}
