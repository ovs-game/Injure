// SPDX-License-Identifier: MIT

using System;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for render pipeline wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GPURenderPipelineHandle {
	internal abstract WGPURenderPipeline WGPURenderPipeline { get; }

	/// <summary>
	/// Returns the underlying <see cref="WebGPU.WGPURenderPipeline"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/Conventions/DangerousGet.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPURenderPipeline DangerousGetNative() => WGPURenderPipeline;
}

/// <summary>
/// Owning wrapper around a render pipeline.
/// </summary>
public sealed class GPURenderPipeline : GPURenderPipelineHandle, IDisposable {
	private WGPURenderPipeline renderPipeline;

	internal GPURenderPipeline(WGPURenderPipeline renderPipeline) {
		this.renderPipeline = renderPipeline;
	}

	internal override WGPURenderPipeline WGPURenderPipeline => renderPipeline;

	/// <summary>
	/// Creates a non-owning view of this render pipeline.
	/// </summary>
	public GPURenderPipelineRef AsRef() => new GPURenderPipelineRef(this);

	/// <summary>
	/// Releases the underlying WebGPU render pipeline.
	/// </summary>
	public void Dispose() {
		if (renderPipeline.IsNotNull)
			wgpuRenderPipelineRelease(renderPipeline);
		renderPipeline = default;
	}
}

/// <summary>
/// Non-owning wrapper around a render pipeline.
/// </summary>
public sealed class GPURenderPipelineRef : GPURenderPipelineHandle {
	private readonly GPURenderPipeline source;
	internal GPURenderPipelineRef(GPURenderPipeline source) {
		this.source = source;
	}

	internal override WGPURenderPipeline WGPURenderPipeline => source.WGPURenderPipeline;
}

/// <summary>
/// Parameters used to create a <see cref="GPURenderPipeline"/>.
/// </summary>
/// <param name="Layout">Layout for this pipeline.</param>
/// <param name="Vertex">Vertex state.</param>
/// <param name="Fragment">
/// Fragment state; may be <see langword="null"/> for vertex-only pipelines.
/// </param>
/// <param name="Primitive">
/// Primitive state, or <see langword="null"/> for the default value of
/// <c>new PrimitiveState()</c>.
/// </param>
/// <param name="DepthStencil">
/// Depth/stencil state; may be <see langword="null"/> for color-only pipelines.
/// </param>
/// <param name="Multisample">
/// Multisample state, or <see langword="null"/> for the default value of
/// <c>new MultisampleState()</c>.
/// </param>
public readonly record struct GPURenderPipelineCreateParams(
	GPUPipelineLayoutHandle Layout,
	VertexState Vertex,
	FragmentState? Fragment = null,
	PrimitiveState? Primitive = null,
	DepthStencilState? DepthStencil = null,
	MultisampleState? Multisample = null
);
