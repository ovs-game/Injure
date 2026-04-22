// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Describes a color attachment in a render pipeline.
/// </summary>
/// <param name="Format">Color attachment format.</param>
/// <param name="Blend">Optional blend state; use <see langword="null"/> for none.</param>
/// <param name="WriteMask">Enabled color channels for writes.</param>
public readonly record struct ColorTargetState(
	TextureFormat Format,
	BlendState? Blend,
	ColorWriteMask WriteMask
) {
	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUColorTargetState"/>.
	/// </summary>
	/// <remarks>
	/// Because the <see cref="WGPUColorTargetState.blend"/> field is a pointer,
	/// requires passing in a scratch-buffer storage pointer. That pointer must be
	/// kept alive together with the returned value.
	/// </remarks>
	public unsafe WGPUColorTargetState ToWebGPUType(WGPUBlendState *blendStorage) {
		WGPUColorTargetState ret = new WGPUColorTargetState {
			format = Format.ToWebGPUType(),
			writeMask = WriteMask.ToWebGPUType()
		};
		if (Blend is BlendState b) {
			*blendStorage = b.ToWebGPUType();
			ret.blend = blendStorage;
		}
		return ret;
	}
};
