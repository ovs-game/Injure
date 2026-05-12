// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum(CheckZeroName = false)]
[ClosedEnumMirror(typeof(WebGPU.WGPUSamplerBindingType))]
public readonly partial struct SamplerBindingType {
	public enum Case {
		BindingNotUsed = 0,
		Undefined = 1,
		Filtering = 2,
		NonFiltering = 3,
		Comparison = 4,
	}
}
