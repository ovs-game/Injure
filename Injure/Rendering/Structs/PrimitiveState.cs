// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Describes how a render pipeline should construct and rasterize primitives.
/// </summary>
/// <param name="Topology">Primitive topology.</param>
/// <param name="StripIndexFormat">Index format for strip topologies.</param>
/// <param name="FrontFace">Front-face winding rule.</param>
/// <param name="CullMode">Face culling mode.</param>
/// <param name="UnclippedDepth">If true, depth clipping will be disabled.</param>
public readonly record struct PrimitiveState(
	PrimitiveTopology Topology,
	IndexFormat StripIndexFormat = IndexFormat.Undefined,
	FrontFace FrontFace = FrontFace.CCW,
	CullMode CullMode = CullMode.None,
	bool UnclippedDepth = false
) {
	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUPrimitiveState"/>.
	/// </summary>
	public WGPUPrimitiveState ToWebGPUType() => new WGPUPrimitiveState {
		topology = Topology.ToWebGPUType(),
		stripIndexFormat = StripIndexFormat.ToWebGPUType(),
		frontFace = FrontFace.ToWebGPUType(),
		cullMode = CullMode.ToWebGPUType(),
		unclippedDepth = UnclippedDepth ? WGPUBool.True : WGPUBool.False
	};
}
