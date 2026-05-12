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
	IndexFormat StripIndexFormat,
	FrontFace FrontFace,
	CullMode CullMode,
	bool UnclippedDepth
) {
	public PrimitiveState(PrimitiveTopology Topology) : this(Topology, IndexFormat.Undefined, FrontFace.CCW, CullMode.None, false) {
	}
	public PrimitiveState(PrimitiveTopology Topology, FrontFace FrontFace, CullMode CullMode) :
		this(Topology, IndexFormat.Undefined, FrontFace, CullMode, false) {
	}

	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUPrimitiveState"/>.
	/// </summary>
	public WGPUPrimitiveState ToWebGPUType() => new() {
		topology = Topology.ToWebGPUType(),
		stripIndexFormat = StripIndexFormat.ToWebGPUType(),
		frontFace = FrontFace.ToWebGPUType(),
		cullMode = CullMode.ToWebGPUType(),
		unclippedDepth = UnclippedDepth ? WGPUBool.True : WGPUBool.False,
	};
}
