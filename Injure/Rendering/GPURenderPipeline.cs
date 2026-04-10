// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Owning wrapper around a render pipeline.
/// </summary>
public sealed unsafe class GPURenderPipeline(WebGPUDevice device, RenderPipeline *pipeline) : IDisposable {
	private readonly WebGPUDevice device = device;

	internal RenderPipeline *Pipeline { get; private set; } = pipeline;

	/// <summary>
	/// Returns the underlying <see cref="RenderPipeline"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public RenderPipeline *DangerousGetPtr() => Pipeline;

	/// <summary>
	/// Releases the underlying WebGPU render pipeline.
	/// </summary>
	public void Dispose() {
		if (Pipeline is not null)
			device.API.RenderPipelineRelease(Pipeline);
		Pipeline = null;
	}
}

/// <summary>
/// Parameters used to create a <see cref="GPURenderPipeline"/>.
/// </summary>
/// <param name="Shader">Shader module supplying the vertex and fragment shaders.</param>
/// <param name="VertShaderEntryPoint">Vertex shader entry point name.</param>
/// <param name="FragShaderEntryPoint">Fragment shader entry point name.</param>
/// <param name="VertexStride">Stride of one vertex in bytes.</param>
/// <param name="VertexStepMode">Whether the vertex buffer advances per vertex or per instance.</param>
/// <param name="VertexAttributes">Vertex attribute declarations for the vertex buffer.</param>
/// <param name="PrimitiveTopology">Primitive topology used by the pipeline.</param>
/// <param name="FrontFace">Front-face winding rule.</param>
/// <param name="CullMode">Face-culling mode.</param>
/// <param name="ColorTargetFormat">Color attachment format this pipeline targets.</param>
/// <param name="Blend">Optional color blend state.</param>
/// <param name="ColorWriteMask">Color write mask, selecting the enabled color channels for writes.</param>
/// <param name="DepthStencil">Optional depth/stencil state.</param>
/// <remarks>
/// Pipelines are color-target-format-specific; rendering to different color
/// formats typically requires separate pipelines.
/// </remarks>
public readonly record struct GPURenderPipelineCreateParams(
	GPUShader Shader,
	string VertShaderEntryPoint,
	string FragShaderEntryPoint,
	ulong VertexStride,
	VertexStepMode VertexStepMode,
	VertexAttribute[] VertexAttributes,
	PrimitiveTopology PrimitiveTopology,
	FrontFace FrontFace,
	CullMode CullMode,
	TextureFormat ColorTargetFormat,
	BlendState? Blend = null,
	ColorWriteMask ColorWriteMask = ColorWriteMask.All,
	DepthStencilState? DepthStencil = null
);
