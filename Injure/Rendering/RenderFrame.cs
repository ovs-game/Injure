// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using WebGPU;
using static WebGPU.WebGPU;

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
public sealed unsafe class RenderFrame : IDisposable {
	private readonly WebGPUDevice device;
	private readonly WGPUSurfaceTexture primaryTex;
	private readonly GPUTextureView primaryView;
	private readonly WGPUCommandEncoder encoder;
	private readonly Action presentCallback;
	private readonly List<IDisposable> deferred = new();
	private bool activepass = false;
	private bool done = false;

	internal RenderFrame(WebGPUDevice device, WGPUSurfaceTexture primaryTex, GPUTextureView primaryView, WGPUCommandEncoder encoder, Action presentCallback) {
		this.device = device;
		this.primaryTex = primaryTex;
		this.primaryView = primaryView;
		PrimaryView = primaryView.AsRef();
		this.encoder = encoder;
		this.presentCallback = presentCallback;
	}

	/// <summary>
	/// The color view for this frame's primary output.
	/// </summary>
	/// <remarks>
	/// The primary output is color-only. Passes that require depth/stencil
	/// should use an offscreen render target instead.
	/// </remarks>
	public GPUTextureViewRef PrimaryView => done ? throw new InvalidOperationException("frame already submitted/disposed") : field;

	private void onPassFinished() {
		if (!activepass)
			throw new InternalStateException("onPassFinished called but no pass is currently active");
		activepass = false;
	}

	private RenderPass beginPass(WGPUCommandEncoder enc, WGPUTextureView colorView, WGPUTextureView depthStencilView,
		in ColorAttachmentOps colorOps, in DepthAttachmentOps? depthOps, in StencilAttachmentOps? stencilOps) {
		if (done)
			throw new InvalidOperationException("frame already submitted/disposed");
		if (activepass)
			throw new InvalidOperationException("frame already has an active pass");

		WGPURenderPassColorAttachment *colorAttachments = stackalloc WGPURenderPassColorAttachment[1];
		colorAttachments[0] = new WGPURenderPassColorAttachment {
			view = colorView,
			loadOp = colorOps.LoadOp.ToWebGPUType(),
			storeOp = colorOps.StoreOp.ToWebGPUType(),
			clearValue = colorOps.ClearValue.ToWebGPUColor(),
			depthSlice = WGPU_DEPTH_SLICE_UNDEFINED
		};

		WGPURenderPassDescriptor desc = new() {
			colorAttachmentCount = 1,
			colorAttachments = colorAttachments
		};

		if (depthStencilView.IsNotNull) {
			WGPURenderPassDepthStencilAttachment *depthStencilAttachment = stackalloc WGPURenderPassDepthStencilAttachment[1];
			DepthAttachmentOps d = depthOps ?? throw new ArgumentNullException(nameof(depthOps));
			depthStencilAttachment[0] = new WGPURenderPassDepthStencilAttachment {
				view = depthStencilView,
				depthLoadOp = d.LoadOp.ToWebGPUType(),
				depthStoreOp = d.StoreOp.ToWebGPUType(),
				depthClearValue = d.ClearValue,
				depthReadOnly = false
			};
			if (stencilOps is StencilAttachmentOps st) {
				depthStencilAttachment[0].stencilLoadOp = st.LoadOp.ToWebGPUType();
				depthStencilAttachment[0].stencilStoreOp = st.StoreOp.ToWebGPUType();
				depthStencilAttachment[0].stencilClearValue = st.ClearValue;
				depthStencilAttachment[0].stencilReadOnly = false;
			} else {
				depthStencilAttachment[0].stencilLoadOp = WGPULoadOp.Undefined;
				depthStencilAttachment[0].stencilStoreOp = WGPUStoreOp.Undefined;
				depthStencilAttachment[0].stencilClearValue = 0;
				depthStencilAttachment[0].stencilReadOnly = true;
			}
			desc.depthStencilAttachment = depthStencilAttachment;
		}

		WGPURenderPassEncoder passEnc = WebGPUException.Check(wgpuCommandEncoderBeginRenderPass(enc, &desc));
		activepass = true;
		return new RenderPass(passEnc, onPassFinished);
	}

	[StackTraceHidden]
	private static void validateView(GPUTextureViewHandle view, string paramName) {
		ArgumentNullException.ThrowIfNull(view);
		if (view.Usage.HasNone(TextureUsage.RenderAttachment))
			throw new ArgumentException("view must have RenderAttachment set in its usages", paramName);
		if (view.Dimension != TextureViewDimension.Dimension2D)
			throw new ArgumentException("view must be 2D", paramName);
	}

	[StackTraceHidden]
	private static void validateColorView(GPUTextureViewHandle colorView, string paramName) {
		validateView(colorView, paramName);
		if (colorView.Format.Tag is TextureFormat.Case.Depth16Unorm or TextureFormat.Case.Depth24Plus or TextureFormat.Case.Depth32Float
			or TextureFormat.Case.Depth24PlusStencil8 or TextureFormat.Case.Depth32FloatStencil8 or TextureFormat.Case.Stencil8)
			throw new ArgumentException("color view must be a color format", paramName);
	}

	[StackTraceHidden]
	private static void validateDepthView(GPUTextureViewHandle depthView, string paramName) {
		validateView(depthView, paramName);
		if (!(depthView.Format.Tag is TextureFormat.Case.Depth16Unorm or TextureFormat.Case.Depth24Plus or TextureFormat.Case.Depth32Float))
			throw new ArgumentException("depth view must be a depth-only format (no stencil)", paramName);
	}

	[StackTraceHidden]
	private static void validateDepthStencilView(GPUTextureViewHandle depthStencilView, string paramName) {
		validateView(depthStencilView, paramName);
		if (!(depthStencilView.Format.Tag is TextureFormat.Case.Depth24PlusStencil8 or TextureFormat.Case.Depth32FloatStencil8))
			throw new ArgumentException("depth+stencil view must be a depth+stencil format", paramName);
	}

	[StackTraceHidden]
	private static void validateCompatibleAttachments(GPUTextureViewHandle a, GPUTextureViewHandle b) {
		if (a.Width != b.Width || a.Height != b.Height)
			throw new ArgumentException("attachment views must have equal dimensions");
		if (a.SampleCount != b.SampleCount)
			throw new ArgumentException("attachment views must have equal sample counts");
	}

	/// <summary>
	/// Opens a render pass targeting a 2D color texture view.
	/// </summary>
	/// <param name="colorView">Color texture view to use.</param>
	/// <param name="colorOps">Color attachment load/store operations.</param>
	/// <remarks>
	/// The returned <see cref="RenderPass"/> object must be disposed before the
	/// frame can be submitted or disposed.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="colorView"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="colorView"/> doesn't have
	/// <see cref="TextureUsage.RenderAttachment"/> set in its usages, isn't 2D, or
	/// is a non-color format.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the frame has already been submitted/disposed, or if another
	/// render pass is already active.
	/// </exception>
	public RenderPass BeginColorPass(GPUTextureViewHandle colorView, in ColorAttachmentOps colorOps) {
		validateColorView(colorView, nameof(colorView));
		return beginPass(encoder, colorView.WGPUTextureView, default, in colorOps, null, null);
	}

	/// <summary>
	/// Opens a render pass targeting a 2D color texture view and 2D depth texture view pair.
	/// </summary>
	/// <param name="colorView">Color texture view to use.</param>
	/// <param name="colorOps">Color attachment load/store operations.</param>
	/// <param name="depthView">Depth texture view to use.</param>
	/// <param name="depthOps">Depth attachment load/store operations.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="colorView"/> or <paramref name="depthView"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="colorView"/> or <paramref name="depthView"/> don't have
	/// <see cref="TextureUsage.RenderAttachment"/> set in their usages, aren't 2D, have
	/// unequal dimensions/sample counts, if <paramref name="colorView"/> is a non-color
	/// format, or if <paramref name="depthView"/> is a non-depth-only format.
	/// </exception>
	/// <inheritdoc cref="BeginColorPass(GPUTextureViewHandle, in ColorAttachmentOps)"/>
	public RenderPass BeginColorDepthPass(GPUTextureViewHandle colorView, in ColorAttachmentOps colorOps,
		GPUTextureViewHandle depthView, in DepthAttachmentOps depthOps) {
		validateColorView(colorView, nameof(colorView));
		validateDepthView(depthView, nameof(depthView));
		validateCompatibleAttachments(colorView, depthView);
		return beginPass(encoder, colorView.WGPUTextureView, depthView.WGPUTextureView, in colorOps, depthOps, null);
	}

	/// <summary>
	/// Opens a render pass targeting a 2D color texture view and 2D depth+stencil texture view pair.
	/// </summary>
	/// <param name="colorView">Color texture view to use.</param>
	/// <param name="colorOps">Color attachment load/store operations.</param>
	/// <param name="depthStencilView">Depth+stencil texture view to use.</param>
	/// <param name="depthOps">Depth attachment load/store operations.</param>
	/// <param name="stencilOps">Stencil attachment load/store operations.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="colorView"/> or <paramref name="depthStencilView"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="colorView"/> or <paramref name="depthStencilView"/> don't have
	/// <see cref="TextureUsage.RenderAttachment"/> set in their usages, aren't 2D, have
	/// unequal dimensions/sample counts, if <paramref name="colorView"/> is a non-color
	/// format, or <paramref name="depthStencilView"/> is a non-depth+stencil format.
	/// </exception>
	/// <inheritdoc cref="BeginColorPass(GPUTextureViewHandle, in ColorAttachmentOps)"/>
	public RenderPass BeginColorDepthStencilPass(GPUTextureViewHandle colorView, in ColorAttachmentOps colorOps,
		GPUTextureViewHandle depthStencilView, in DepthAttachmentOps depthOps, in StencilAttachmentOps stencilOps) {
		validateColorView(colorView, nameof(colorView));
		validateDepthStencilView(depthStencilView, nameof(depthStencilView));
		validateCompatibleAttachments(colorView, depthStencilView);
		return beginPass(encoder, colorView.WGPUTextureView, depthStencilView.WGPUTextureView, in colorOps, depthOps, stencilOps);
	}

	/// <summary>
	/// Convenience method to open a render pass targeting the primary output,
	/// equivalent to <see cref="BeginColorPass(GPUTextureViewHandle, in ColorAttachmentOps)"/>
	/// with <see cref="PrimaryView"/>.
	/// </summary>
	/// <inheritdoc cref="BeginColorPass(GPUTextureViewHandle, in ColorAttachmentOps)"/>
	public RenderPass BeginPrimaryPass(in ColorAttachmentOps colorOps) =>
		BeginColorPass(PrimaryView, in colorOps);

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

		WGPUCommandBufferDescriptor desc;
		WGPUCommandBuffer cmdbuf = WebGPUException.Check(wgpuCommandEncoderFinish(encoder, &desc));

		device.Submit(cmdbuf);
		presentCallback();

		wgpuCommandBufferRelease(cmdbuf);
		wgpuCommandEncoderRelease(encoder);
		primaryView.Dispose();
		wgpuTextureRelease(primaryTex.texture);
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
		wgpuCommandEncoderRelease(encoder);
		primaryView.Dispose();
		wgpuTextureRelease(primaryTex.texture);
		foreach (IDisposable disp in deferred)
			disp.Dispose();
		deferred.Clear();
	}
}
