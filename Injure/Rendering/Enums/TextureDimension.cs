// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUTextureDimension))]
public readonly partial struct TextureDimension {
	public enum Case {
		Undefined = 0,
		Dimension1D = 1,
		Dimension2D = 2,
		Dimension3D = 3
	}
}
