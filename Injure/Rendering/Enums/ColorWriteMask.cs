// SPDX-License-Identifier: MIT

using System;

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedFlags]
[ClosedFlagsMirror(typeof(WebGPU.WGPUColorWriteMask))]
public readonly partial struct ColorWriteMask {
	[Flags]
	public enum Bits : ulong {
		None = 0ul,
		Red = 1ul,
		Green = 2ul,
		Blue = 4ul,
		Alpha = 8ul,
		All = 15ul
	}
}
