// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUStencilOperation))]
public readonly partial struct StencilOperation {
	public enum Case {
		Undefined = 0,
		Keep = 1,
		Zero = 2,
		Replace = 3,
		Invert = 4,
		IncrementClamp = 5,
		DecrementClamp = 6,
		IncrementWrap = 7,
		DecrementWrap = 8,
	}
}
