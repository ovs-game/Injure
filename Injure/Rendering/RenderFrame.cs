// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Frame-local command recording scope.
///
/// Owns the command encoder and acquired backbuffer surface resources for one
/// render frame, and allows opening render passes to either the backbuffer or
/// offscreen render targets.
/// </summary>
/// <remarks>
/// Intended to be used as a scope via <c>using</c>.
///
/// Only one <see cref="RenderPass"/> may be active at a time. Temporary
/// resources that must survive up to the submission can be registered with
/// <see cref="DisposeAfterSubmit(IDisposable)"/>.
/// </remarks>
public sealed unsafe class RenderFrame(WebGPURenderer renderer, SurfaceTexture surfaceTex, TextureView *backbufferView, CommandEncoder *encoder) : IDisposable {
	private readonly WebGPURenderer renderer = renderer;
	private readonly SurfaceTexture surfaceTex = surfaceTex;
	private readonly TextureView *backbufferView = backbufferView;
	private readonly CommandEncoder *encoder = encoder;
	private readonly List<IDisposable> deferred = new List<IDisposable>();
	private bool activepass = false;
	private bool done = false;

	private void onPassFinished() {
		if (!activepass)
			throw new InternalStateException("onPassFinished called but no pass is currently active");
		activepass = false;
	}

	private RenderPass beginPass(CommandEncoder *enc, TextureView *colorView, TextureView *depthStencilView, in ColorAttachmentOps colorOps, in DepthStencilAttachmentOps? depthStencilOps) {
		if (done)
			throw new InvalidOperationException("frame already submitted/disposed");
		if (activepass)
			throw new InvalidOperationException("frame already has an active pass");

		RenderPassColorAttachment *colorAttachments = stackalloc RenderPassColorAttachment[1];
		colorAttachments[0] = new RenderPassColorAttachment {
			View = colorView,
			LoadOp = colorOps.LoadOp,
			StoreOp = colorOps.StoreOp,
			ClearValue = colorOps.ClearValue.ToWebGPUColor()
		};

		RenderPassDescriptor desc = new RenderPassDescriptor {
			ColorAttachmentCount = 1,
			ColorAttachments = colorAttachments
		};

		// alloc here so it doesn't go out of scope after the if block
		RenderPassDepthStencilAttachment *depthStencilAttachment = stackalloc RenderPassDepthStencilAttachment[1];
		if (depthStencilView is not null) {
			DepthStencilAttachmentOps ops = depthStencilOps ?? throw new ArgumentNullException(nameof(depthStencilOps));
			depthStencilAttachment[0] = new RenderPassDepthStencilAttachment {
				View = depthStencilView,
				DepthLoadOp = ops.DepthLoadOp,
				DepthStoreOp = ops.DepthStoreOp,
				DepthClearValue = ops.DepthClearValue,
				DepthReadOnly = false,
				StencilLoadOp = ops.StencilLoadOp,
				StencilStoreOp = ops.StencilStoreOp,
				StencilClearValue = ops.StencilClearValue,
				StencilReadOnly = true
			};
			desc.DepthStencilAttachment = depthStencilAttachment;
		} else if (depthStencilOps is not null) {
			throw new ArgumentException("depthStencilOps were provided, but the render target has no depth/stencil attachment");
		}

		RenderPassEncoder *passEnc = WebGPUException.Check(renderer.webgpu.CommandEncoderBeginRenderPass(enc, &desc));
		activepass = true;
		return new RenderPass(renderer, passEnc, onPassFinished);
	}

	/// <summary>
	/// Opens a render pass targeting the backbuffer view acquired for this frame.
	/// </summary>
	/// <param name="colorOps">Color attachment load/store operations.</param>
	/// <remarks>
	/// The returned <see cref="RenderPass"/> object must be disposed before the
	/// frame can be submitted or disposed.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has already been submitted/disposed, or if another
	/// render pass is already active.
	/// </exception>
	public RenderPass BeginBackbufferPass(in ColorAttachmentOps colorOps) {
		return beginPass(encoder, backbufferView, null, colorOps, null);
	}

	/// <summary>
	/// Opens a render pass targeting the given offscreen render target.
	/// </summary>
	/// <param name="rt">Render target whose color view will be used as the pass's color attachment.</param>
	/// <param name="colorOps">Color attachment load/store operations.</param>
	/// <param name="depthStencilOps">Depth/stencil attachment load/store operations, if applicable.</param>
	/// <remarks>
	/// If <paramref name="rt"/> has a depth/stencil attachment, it is bound
	/// automatically alongside the color attachment.
	/// The returned <see cref="RenderPass"/> object must be disposed before the
	/// frame can be submitted or disposed.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="rt"/> is <see langword="null"/>, or if
	/// <paramref name="rt"/> has a depth/stencil attachment but
	/// <paramref name="depthStencilOps"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="depthStencilOps"/> is provided but
	/// <paramref name="rt"/> does not have a depth/stencil attachment.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has already been submitted/disposed, or if another
	/// render pass is already active.
	/// </exception>
	public RenderPass BeginRenderTargetPass(GPURenderTarget rt, in ColorAttachmentOps colorOps, in DepthStencilAttachmentOps? depthStencilOps = null) {
		ArgumentNullException.ThrowIfNull(rt);
		return beginPass(encoder, rt.ColorView, rt.DepthStencilView, colorOps, depthStencilOps);
	}

	/// <summary>
	/// Registers an <see cref="IDisposable"/> for cleanup after this frame is
	/// submitted or discarded.
	/// </summary>
	/// <param name="disp">Object to dispose once the frame is finished.</param>
	/// <remarks>
	/// Typically used for temporary resources that are no longer needed by
	/// CPU code after command recording, but must remain alive until the
	/// submit is complete because encoded GPU work references them.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="disp"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has already been submitted/disposed.
	/// </exception>
	public void DisposeAfterSubmit(IDisposable disp) {
		ArgumentNullException.ThrowIfNull(disp);
		if (done)
			throw new InvalidOperationException("frame already submitted/disposed");
		deferred.Add(disp);
	}

	/// <summary>
	/// Finishes command recording, submits the frame, and presents the currently
	/// acquired backbuffer surface texture.
	/// </summary>
	/// <remarks>
	/// Any disposables registered with <see cref="DisposeAfterSubmit(IDisposable)"/>
	/// are disposed once submission is complete.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has already been submitted/disposed, or if a render
	/// pass is still active.
	/// </exception>
	public void SubmitAndPresent() {
		if (done)
			throw new InvalidOperationException("frame already submitted/disposed");
		if (activepass)
			throw new InvalidOperationException("frame still has an unclosed render pass");

		CommandBufferDescriptor desc;
		CommandBuffer *cmdbuf = WebGPUException.Check(renderer.webgpu.CommandEncoderFinish(encoder, &desc));

		renderer.Submit(cmdbuf);
		renderer.Present();

		renderer.webgpu.CommandBufferRelease(cmdbuf);
		renderer.webgpu.CommandEncoderRelease(encoder);
		renderer.webgpu.TextureViewRelease(backbufferView);
		renderer.webgpu.TextureRelease(surfaceTex.Texture);
		foreach (IDisposable disp in deferred)
			disp.Dispose();
		deferred.Clear();
		done = true;
	}

	/// <summary>
	/// Discards the frame if it has not already been submitted.
	/// </summary>
	/// <remarks>
	/// Callers should structure usage such that this always runs, typically via
	/// <c>using</c>, rather than only disposing on non-submit paths.
	///
	/// Any disposables registered with <see cref="DisposeAfterSubmit(IDisposable)"/>
	/// are disposed.
	/// Disposing a frame with an active pass is a bug and throws instead of
	/// silently closing the pass.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if a render pass is still active.
	/// </exception>
	public void Dispose() {
		if (done)
			return;
		if (activepass)
			throw new InvalidOperationException("frame still has an unclosed render pass - this could be automatically cleaned up, but if it happened you probably have a bug");

		done = true;
		renderer.webgpu.CommandEncoderRelease(encoder);
		renderer.webgpu.TextureViewRelease(backbufferView);
		renderer.webgpu.TextureRelease(surfaceTex.Texture);
		foreach (IDisposable disp in deferred)
			disp.Dispose();
		deferred.Clear();
	}
}
