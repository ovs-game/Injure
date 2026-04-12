// SPDX-License-Identifier: MIT

using System;

namespace Injure.Rendering;

[Flags]
public enum ColorWriteMask : ulong {
	None = 0ul,
	Red = 1ul,
	Green = 2ul,
	Blue = 4ul,
	Alpha = 8ul,
	All = 15ul
}
