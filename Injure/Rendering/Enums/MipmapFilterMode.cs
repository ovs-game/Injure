// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUMipmapFilterMode))]
public readonly partial struct MipmapFilterMode {
	public enum Case {
		Undefined = 0,
		Nearest = 1,
		Linear = 2
	}
}
