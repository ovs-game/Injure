// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Rendering;

/// <summary>
/// Vertex stage state for a render pipeline.
/// </summary>
/// <param name="ShaderModule">Shader module containing the vertex entry point.</param>
/// <param name="EntryPoint">Vertex entry point name.</param>
/// <param name="Buffers">Vertex buffer layouts used by the vertex stage.</param>
public readonly record struct VertexState(
	GPUShaderModuleHandle ShaderModule,
	string EntryPoint,
	ImmutableArray<VertexBufferLayout> Buffers = default
);
