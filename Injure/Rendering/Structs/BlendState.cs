// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Describes color and alpha blending state for a color target.
/// </summary>
/// <param name="Color">Blend behavior for the color components.</param>
/// <param name="Alpha">Blend behavior for the alpha component.</param>
public readonly record struct BlendState(
	BlendComponent Color,
	BlendComponent Alpha
) {
	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUBlendState"/>.
	/// </summary>
	public WGPUBlendState ToWebGPUType() => new() {
		color = Color.ToWebGPUType(),
		alpha = Alpha.ToWebGPUType(),
	};
}
