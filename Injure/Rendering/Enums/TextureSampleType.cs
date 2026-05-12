// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum(CheckZeroName = false)]
[ClosedEnumMirror(typeof(WebGPU.WGPUTextureSampleType))]
public readonly partial struct TextureSampleType {
	public enum Case {
		BindingNotUsed = 0,
		Undefined = 1,
		Float = 2,
		UnfilterableFloat = 3,
		Depth = 4,
		Sint = 5,
		Uint = 6,
	}
}
