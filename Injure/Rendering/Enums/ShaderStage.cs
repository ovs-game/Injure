// SPDX-License-Identifier: MIT

using System;

namespace Injure.Rendering;

[Flags]
public enum ShaderStage : ulong {
	None = 0ul,
	Vertex = 1ul,
	Fragment = 2ul,
	Compute = 4ul
}
