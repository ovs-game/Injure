// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Frame-local command recording scope.
/// Owns the command encoder and acquired primary output for one render frame,
/// and allows opening render passes to either the primary output or offscreen
/// render targets.
/// </summary>
/// <remarks>
/// <para>
/// Intended to be used as a scope via <c>using</c>. If a frame is disposed without
/// ever being submitted, the recorded work is discarded and acquired output resources
/// are released without presentation.
/// </para>
/// <para>
/// Only one <see cref="RenderPass"/> may be active at a time. Temporary
/// resources that must survive up to the submission (or discard via disposal)
/// can be registered with <see cref="DisposeAfterSubmit(IDisposable)"/>.
/// </para>
/// </remarks>
public sealed unsafe class RenderFrame(WebGPUDevice device, SurfaceTexture primaryTex, TextureView *primaryView, CommandEncoder *encoder,
	Action present, uint primaryW, uint primaryH, TextureFormat primaryFormat) : IDisposable {
	private readonly WebGPUDevice device = device;
	private readonly SurfaceTexture primaryTex = primaryTex;
	private readonly TextureView *primaryView = primaryView;
	private readonly CommandEncoder *encoder = encoder;
	private readonly Action present = present;
	private readonly List<IDisposable> deferred = new List<IDisposable>();
	private bool activepass = false;
	private bool done = false;

	/// <summary>
	/// The width of this frame's primary output in physical pixels.
	/// </summary>
	/// <remarks>
	/// This is a snapshot and does not change for the lifetime of the frame.
	/// </remarks>
	public uint PrimaryWidth { get; } = primaryW;

	/// <summary>
	/// The height of this frame's primary output in physical pixels.
	/// </summary>
	/// <remarks>
	/// This is a snapshot and does not change for the lifetime of the frame.
	/// </remarks>
	public uint PrimaryHeight { get; } = primaryH;

	/// <summary>
	/// The color format of this frame's primary output.
	/// </summary>
	public TextureFormat PrimaryFormat { get; } = primaryFormat;

	private void onPassFinished() {
		if (!activepass)
			throw new InternalStateException("onPassFinished called but no pass is currently active");
		activepass = false;
	}

	private RenderPass beginPass(CommandEncoder *enc, TextureView *colorView, TextureView *depthStencilView,
		in ColorAttachmentOps colorOps, in DepthAttachmentOps? depthOps, in StencilAttachmentOps? stencilOps) {
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
			DepthAttachmentOps d = depthOps ?? throw new ArgumentNullException(nameof(depthOps));
			depthStencilAttachment[0] = new RenderPassDepthStencilAttachment {
				View = depthStencilView,
				DepthLoadOp = d.LoadOp,
				DepthStoreOp = d.StoreOp,
				DepthClearValue = d.ClearValue,
				DepthReadOnly = false
			};
			if (stencilOps is StencilAttachmentOps st) {
				depthStencilAttachment[0].StencilLoadOp = st.LoadOp;
				depthStencilAttachment[0].StencilStoreOp = st.StoreOp;
				depthStencilAttachment[0].StencilClearValue = st.ClearValue;
				depthStencilAttachment[0].StencilReadOnly = false;
			} else {
				depthStencilAttachment[0].StencilLoadOp = LoadOp.Undefined;
				depthStencilAttachment[0].StencilStoreOp = StoreOp.Undefined;
				depthStencilAttachment[0].StencilClearValue = 0;
				depthStencilAttachment[0].StencilReadOnly = true;
			}
			desc.DepthStencilAttachment = depthStencilAttachment;
		}

		RenderPassEncoder *passEnc = WebGPUException.Check(device.API.CommandEncoderBeginRenderPass(enc, &desc));
		activepass = true;
		return new RenderPass(device, passEnc, onPassFinished);
	}

	/// <summary>
	/// Opens a render pass targeting the primary output.
	/// </summary>
	/// <param name="colorOps">Color attachment load/store operations.</param>
	/// <remarks>
	/// <para>
	/// The primary output is color-only. Passes that require depth/stencil
	/// should use an offscreen render target instead.
	/// </para>
	/// <para>
	/// The returned <see cref="RenderPass"/> object must be disposed before the
	/// frame can be submitted or disposed.
	/// </para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has already been submitted/disposed, or if another
	/// render pass is already active.
	/// </exception>
	public RenderPass BeginPrimaryPass(in ColorAttachmentOps colorOps) {
		return beginPass(encoder, primaryView, null, colorOps, null, null);
	}

	/// <summary>
	/// Opens a render pass targeting the given offscreen render target with
	/// no depth/stencil attachment.
	/// </summary>
	/// <param name="rt">Render target to use.</param>
	/// <param name="colorOps">Color attachment load/store operations.</param>
	/// <remarks>
	/// The returned <see cref="RenderPass"/> object must be disposed before the
	/// frame can be submitted or disposed.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="rt"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="rt"/> has a depth/stencil attachment.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has already been submitted/disposed, or if another
	/// render pass is already active.
	/// </exception>
	public RenderPass BeginRenderTargetPass(GPURenderTarget rt, in ColorAttachmentOps colorOps) {
		ArgumentNullException.ThrowIfNull(rt);
		if (rt.HasDepth)
			throw new ArgumentException("expected this render target to not have a depth/stencil attachment; use another overload of this method if it does", nameof(rt));
		return beginPass(encoder, rt.ColorView, null, colorOps, null, null);
	}

	/// <summary>
	/// Opens a render pass targeting the given offscreen render target with
	/// a depth attachment and no stencil attachment.
	/// </summary>
	/// <param name="rt">Render target to use.</param>
	/// <param name="colorOps">Color attachment load/store operations.</param>
	/// <param name="depthOps">Depth attachment load/store operations.</param>
	/// <remarks>
	/// The returned <see cref="RenderPass"/> object must be disposed before the
	/// frame can be submitted or disposed.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="rt"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="rt"/> has no depth attachment, or if its
	/// depth attachment includes stencil.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has already been submitted/disposed, or if another
	/// render pass is already active.
	/// </exception>
	public RenderPass BeginRenderTargetPass(GPURenderTarget rt, in ColorAttachmentOps colorOps, in DepthAttachmentOps depthOps) {
		ArgumentNullException.ThrowIfNull(rt);
		if (!rt.HasDepth)
			throw new ArgumentException("expected this render target to have a depth attachment; use another overload of this method if it doesn't", nameof(rt));
		if (rt.HasStencil)
			throw new ArgumentException("expected this render target to not have a stencil attachment; use another overload of this method if it does", nameof(rt));
		return beginPass(encoder, rt.ColorView, rt.DepthStencilView, colorOps, depthOps, null);
	}

	/// <summary>
	/// Opens a render pass targeting the given offscreen render target with
	/// a depth + stencil attachment.
	/// </summary>
	/// <param name="rt">Render target to use.</param>
	/// <param name="colorOps">Color attachment load/store operations.</param>
	/// <param name="depthOps">Depth attachment load/store operations.</param>
	/// <param name="stencilOps">Stencil attachment load/store operations.</param>
	/// <remarks>
	/// The returned <see cref="RenderPass"/> object must be disposed before the
	/// frame can be submitted or disposed.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="rt"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="rt"/> has no depth attachment or no stencil attachment.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has already been submitted/disposed, or if another
	/// render pass is already active.
	/// </exception>
	public RenderPass BeginRenderTargetPass(GPURenderTarget rt, in ColorAttachmentOps colorOps, in DepthAttachmentOps depthOps, in StencilAttachmentOps stencilOps) {
		ArgumentNullException.ThrowIfNull(rt);
		if (!rt.HasDepth || !rt.HasStencil)
			throw new ArgumentException("expected this render target to have a depth + stencil attachment; use another overload of this method if it doesn't", nameof(rt));
		return beginPass(encoder, rt.ColorView, rt.DepthStencilView, colorOps, depthOps, stencilOps);
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
	/// Finishes command recording, submits the frame, and presents on the primary output.
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
			throw new InvalidOperationException("frame still has an active render pass");

		CommandBufferDescriptor desc;
		CommandBuffer *cmdbuf = WebGPUException.Check(device.API.CommandEncoderFinish(encoder, &desc));

		device.Submit(cmdbuf);
		present();

		device.API.CommandBufferRelease(cmdbuf);
		device.API.CommandEncoderRelease(encoder);
		device.API.TextureViewRelease(primaryView);
		device.API.TextureRelease(primaryTex.Texture);
		foreach (IDisposable disp in deferred)
			disp.Dispose();
		deferred.Clear();
		done = true;
	}

	/// <summary>
	/// Discards the frame if it has not already been submitted.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Callers should structure usage such that this always runs, typically via
	/// <c>using</c>, rather than only disposing on non-submit paths.
	/// </para>
	/// <para>
	/// Any disposables registered with <see cref="DisposeAfterSubmit(IDisposable)"/>
	/// are disposed.
	/// Disposing a frame with an active pass is a bug and throws instead of
	/// silently ending the pass.
	/// </para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if a render pass is still active.
	/// </exception>
	public void Dispose() {
		if (done)
			return;
		if (activepass)
			throw new InvalidOperationException("frame still has an active render pass - this could be automatically cleaned up, but if it happened you probably have a bug");

		done = true;
		device.API.CommandEncoderRelease(encoder);
		device.API.TextureViewRelease(primaryView);
		device.API.TextureRelease(primaryTex.Texture);
		foreach (IDisposable disp in deferred)
			disp.Dispose();
		deferred.Clear();
	}
}
