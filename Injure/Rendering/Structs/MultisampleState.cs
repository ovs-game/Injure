// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Describes how a render pipeline interacts with multisampled attachments.
/// </summary>
/// <param name="Count">Samples per pixel.</param>
/// <param name="Mask">Bitmask determining which samples are written to.</param>
/// <param name="AlphaToCoverageEnabled">Whether alpha-to-coverage is enabled.</param>
public readonly record struct MultisampleState(
	uint Count = 1,
	uint Mask = uint.MaxValue,
	bool AlphaToCoverageEnabled = false
) {
	public MultisampleState() : this(1) {}

	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUMultisampleState"/>.
	/// </summary>
	public WGPUMultisampleState ToWebGPUType() => new WGPUMultisampleState {
		count = Count,
		mask = Mask,
		alphaToCoverageEnabled = AlphaToCoverageEnabled ? WGPUBool.True : WGPUBool.False
	};
}
