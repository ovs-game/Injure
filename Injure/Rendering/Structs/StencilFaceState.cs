// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Describes stencil operations for one face direction.
/// </summary>
/// <param name="Compare">Stencil comparison function.</param>
/// <param name="FailOp">Operation applied when the stencil test fails.</param>
/// <param name="DepthFailOp">Operation applied when the stencil test passes but the depth test fails.</param>
/// <param name="PassOp">Operation applied when both stencil and depth tests pass.</param>
public readonly record struct StencilFaceState(
	CompareFunction Compare = CompareFunction.Always,
	StencilOperation FailOp = StencilOperation.Keep,
	StencilOperation DepthFailOp = StencilOperation.Keep,
	StencilOperation PassOp = StencilOperation.Keep
) {
	public StencilFaceState() : this(CompareFunction.Always) {}

	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUStencilFaceState"/>.
	/// </summary>
	public WGPUStencilFaceState ToWebGPUType() => new WGPUStencilFaceState {
		compare = Compare.ToWebGPUType(),
		failOp = FailOp.ToWebGPUType(),
		depthFailOp = DepthFailOp.ToWebGPUType(),
		passOp = PassOp.ToWebGPUType()
	};
}
