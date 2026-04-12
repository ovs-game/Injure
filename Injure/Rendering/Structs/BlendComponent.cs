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
	BlendOperation Operation = BlendOperation.Add,
	BlendFactor SrcFactor = BlendFactor.One,
	BlendFactor DstFactor = BlendFactor.Zero
) {
	public BlendComponent() : this(BlendOperation.Add) {}

	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUBlendComponent"/>.
	/// </summary>
	public WGPUBlendComponent ToWebGPUType() => new WGPUBlendComponent {
		operation = Operation.ToWebGPUType(),
		srcFactor = SrcFactor.ToWebGPUType(),
		dstFactor = DstFactor.ToWebGPUType()
	};
}
