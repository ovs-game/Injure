// SPDX-License-Identifier: MIT

using System;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public sealed unsafe class GPURenderPipeline(WebGPURenderer renderer, RenderPipeline *pipeline) : IDisposable {
	private readonly WebGPURenderer renderer = renderer;

	internal RenderPipeline *Pipeline { get; private set; } = pipeline;
	public RenderPipeline *DangerousGetPtr() => Pipeline;

	public void Dispose() {
		if (Pipeline is not null)
			renderer.webgpu.RenderPipelineRelease(Pipeline);
		Pipeline = null;
	}
}

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
