// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Describes one vertex attribute within a vertex buffer layout.
/// </summary>
/// <param name="Format">Vertex element format.</param>
/// <param name="Offset">Byte offset of the attribute within one vertex.</param>
/// <param name="ShaderLocation">Shader location consumed by this attribute.</param>
public readonly record struct VertexAttribute(
	VertexFormat Format,
	ulong Offset,
	uint ShaderLocation
) {
	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUVertexAttribute"/>.
	/// </summary>
	public WGPUVertexAttribute ToWebGPUType() => new(
		format: Format.ToWebGPUType(),
		offset: Offset,
		shaderLocation: ShaderLocation
	);
}
