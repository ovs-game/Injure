// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum(CheckZeroName = false)]
[ClosedEnumMirror(typeof(WebGPU.WGPUVertexStepMode))]
public readonly partial struct VertexStepMode {
	public enum Case {
		VertexBufferNotUsed = 0,
		Undefined = 1,
		Vertex = 2,
		Instance = 3
	}
}
