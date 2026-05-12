// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum]
[ClosedEnumMirror(typeof(WebGPU.WGPUTextureViewDimension))]
public readonly partial struct TextureViewDimension {
	public enum Case {
		Undefined = 0,
		Dimension1D = 1,
		Dimension2D = 2,
		Dimension2DArray = 3,
		DimensionCube = 4,
		DimensionCubeArray = 5,
		Dimension3D = 6,
	}
}
