// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Describes a depth/stencil attachment in a render pipeline.
/// </summary>
/// <param name="Format">Depth/stencil attachment.</param>
/// <param name="DepthWriteEnabled">Whether the pipeline can modify depth values.</param>
/// <param name="DepthCompare">Depth comparison function.</param>
/// <param name="StencilFront">Stencil operations for front-facing geometry.</param>
/// <param name="StencilBack">Stencil operations for back-facing geometry.</param>
/// <param name="StencilReadMask">Bitmask applied when reading stencil values.</param>
/// <param name="StencilWriteMask">Bitmask applied when writing stencil values.</param>
/// <param name="DepthBias">Constant depth bias.</param>
/// <param name="DepthBiasSlopeScale">Slope-scaled depth bias factor.</param>
/// <param name="DepthBiasClamp">Clamp applied to the final depth bias.</param>
public readonly record struct DepthStencilState(
	TextureFormat Format,
	bool DepthWriteEnabled,
	CompareFunction DepthCompare,
	StencilFaceState StencilFront,
	StencilFaceState StencilBack,
	uint StencilReadMask = uint.MaxValue,
	uint StencilWriteMask = uint.MaxValue,
	int DepthBias = 0,
	float DepthBiasSlopeScale = 0f,
	float DepthBiasClamp = 0f
) {
	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUDepthStencilState"/>.
	/// </summary>
	public WGPUDepthStencilState ToWebGPUType() => new() {
		format = Format.ToWebGPUType(),
		depthWriteEnabled = DepthWriteEnabled ? WGPUOptionalBool.True : WGPUOptionalBool.False,
		depthCompare = DepthCompare.ToWebGPUType(),
		stencilFront = StencilFront.ToWebGPUType(),
		stencilBack = StencilBack.ToWebGPUType(),
		stencilReadMask = StencilReadMask,
		stencilWriteMask = StencilWriteMask,
		depthBias = DepthBias,
		depthBiasSlopeScale = DepthBiasSlopeScale,
		depthBiasClamp = DepthBiasClamp
	};
}
