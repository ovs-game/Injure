// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum(CheckZeroName = false)]
[ClosedEnumMirror(typeof(WebGPU.WGPUBufferBindingType))]
public readonly partial struct BufferBindingType {
	public enum Case {
		BindingNotUsed = 0,
		Undefined = 1,
		Uniform = 2,
		Storage = 3,
		ReadOnlyStorage = 4
	}
}
