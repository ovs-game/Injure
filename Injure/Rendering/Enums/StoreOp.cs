// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUStoreOp))]
public readonly partial struct StoreOp {
	public enum Case {
		Undefined = 0,
		Store = 1,
		Discard = 2
	}
}
