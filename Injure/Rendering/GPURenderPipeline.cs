// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for render pipeline wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract unsafe class GPURenderPipelineHandle {
	internal abstract RenderPipeline *RenderPipeline { get; }

	/// <summary>
	/// Returns the underlying <see cref="Silk.NET.WebGPU.RenderPipeline"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	public RenderPipeline *DangerousGetPtr() => RenderPipeline;
}

/// <summary>
/// Owning wrapper around a render pipeline.
/// </summary>
public sealed unsafe class GPURenderPipeline : GPURenderPipelineHandle, IDisposable {
	private readonly WebGPUDevice device;
	private RenderPipeline *renderPipeline;

	internal GPURenderPipeline(WebGPUDevice device, RenderPipeline *renderPipeline) {
		this.device = device;
		this.renderPipeline = renderPipeline;
	}

	internal override RenderPipeline *RenderPipeline => renderPipeline;

	/// <summary>
	/// Creates a non-owning view of this render pipeline.
	/// </summary>
	public GPURenderPipelineRef AsRef() => new GPURenderPipelineRef(this);

	/// <summary>
	/// Releases the underlying WebGPU render pipeline.
	/// </summary>
	public void Dispose() {
		if (renderPipeline is not null)
			device.API.RenderPipelineRelease(renderPipeline);
		renderPipeline = null;
	}
}

/// <summary>
/// Non-owning wrapper around a render pipeline.
/// </summary>
public sealed unsafe class GPURenderPipelineRef : GPURenderPipelineHandle {
	private readonly GPURenderPipeline source;
	internal GPURenderPipelineRef(GPURenderPipeline source) {
		this.source = source;
	}

	internal override RenderPipeline *RenderPipeline => source.RenderPipeline;
}

/// <summary>
/// Parameters used to create a <see cref="GPURenderPipeline"/>.
/// </summary>
/// <param name="ShaderModule">Shader module supplying the vertex and fragment shaders.</param>
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
	GPUShaderModuleHandle ShaderModule,
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
