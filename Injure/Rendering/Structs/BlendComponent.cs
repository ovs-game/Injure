// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Describes how the color/alpha of a fragment is blended.
/// </summary>
/// <param name="Operation">Blend operation to apply.</param>
/// <param name="SrcFactor">Factor applied to the source value.</param>
/// <param name="DstFactor">Factor applied to the destination value.</param>
public readonly record struct BlendComponent(
	BlendOperation Operation,
	BlendFactor SrcFactor,
	BlendFactor DstFactor
) {
	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUBlendComponent"/>.
	/// </summary>
	public WGPUBlendComponent ToWebGPUType() => new() {
		operation = Operation.ToWebGPUType(),
		srcFactor = SrcFactor.ToWebGPUType(),
		dstFactor = DstFactor.ToWebGPUType()
	};
}
