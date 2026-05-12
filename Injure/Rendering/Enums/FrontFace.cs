// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUFrontFace))]
public readonly partial struct FrontFace {
	public enum Case {
	Undefined = 0,
		CCW = 1,
		CW = 2,
	}
}
