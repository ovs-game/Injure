// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Policy for selecting a surface present mode in <see cref="SurfaceRenderOutput"/>.
/// </summary>
public enum SurfacePresentModePolicy {
	/// <summary>
	/// Prefer <see cref="PresentMode.Mailbox"/>, fall back to <see cref="PresentMode.Fifo"/>
	/// if not present.
	/// </summary>
	/// <remarks>
	/// Tear-free.
	/// </remarks>
	AutoMailbox,

	/// <summary>
	/// Prefer <see cref="PresentMode.FifoRelaxed"/>, fall back to <see cref="PresentMode.Mailbox"/>
	/// and then <see cref="PresentMode.Fifo"/> if not present.
	/// </summary>
	/// <remarks>
	/// Normally tear-free; may tear if a frame remains on the frontbuffer for more than one vblank.
	/// </remarks>
	AutoRelaxedFifo,

	/// <summary>
	/// Prefer <see cref="PresentMode.Immediate"/>, fall back to <see cref="PresentMode.Mailbox"/>,
	/// then <see cref="PresentMode.FifoRelaxed">, and finally <see cref="PresentMode.Fifo"/>
	/// if not present.
	/// </summary>
	/// <remarks>
	/// May tear. Lowest latency.
	/// </remarks>
	AutoImmediate
}

public sealed unsafe class SurfaceRenderOutput : IRenderOutput {
	private readonly WebGPUDevice device;
	private readonly ISurfaceHost surfaceHost;
	private readonly SurfacePresentModePolicy presentPolicy;

	private Surface *surface;
	private TextureFormat format;
	private PresentMode presentMode;
	private SurfaceConfiguration config;
	private bool disposed = false;

	public uint Width { get { ObjectDisposedException.ThrowIf(disposed, this); return field; } private set; }
	public uint Height { get { ObjectDisposedException.ThrowIf(disposed, this); return field; } private set; }
	public TextureFormat Format { get { ObjectDisposedException.ThrowIf(disposed, this); return format; } }

	public SurfaceRenderOutput(WebGPUDevice device, ISurfaceHost surfaceHost, SurfacePresentModePolicy presentPolicy) {
		this.device = device;
		this.surfaceHost = surfaceHost;
		this.presentPolicy = presentPolicy;

		SurfaceDescriptorContainer sdc;
		surfaceHost.CreateSurfaceDesc(&sdc);
		surface = device.API.InstanceCreateSurface(device.Instance, &sdc.Desc);
		format = getSurfaceFormat();
		presentMode = getSurfacePresentMode();
		queryAndResize();
	}

	private TextureFormat getSurfaceFormat() {
		SurfaceCapabilities caps;
		device.API.SurfaceGetCapabilities(surface, device.Adapter, &caps);
		try {
			if (caps.FormatCount == 0)
				throw new WebGPUException("SurfaceGetCapabilities", "surface doesn't report any supported formats");
			// wgpu says the first format is the most preferred one
			return caps.Formats[0];
		} finally {
			device.API.SurfaceCapabilitiesFreeMembers(caps);
		}
	}

	private PresentMode getSurfacePresentMode() {
		SurfaceCapabilities caps;
		device.API.SurfaceGetCapabilities(surface, device.Adapter, &caps);
		try {
			if (caps.PresentModeCount == 0)
				throw new WebGPUException("SurfaceGetCapabilities", "surface doesn't report any supported present modes");
			ReadOnlySpan<PresentMode> modes = new ReadOnlySpan<PresentMode>(caps.PresentModes, (int)caps.PresentModeCount);
			bool haverelaxed = modes.Contains(PresentMode.FifoRelaxed);
			bool havemailbox = modes.Contains(PresentMode.Mailbox);
			bool haveimmediate = modes.Contains(PresentMode.Immediate);
			switch (presentPolicy) {
			case SurfacePresentModePolicy.AutoMailbox:
				return havemailbox ? PresentMode.Mailbox : PresentMode.Fifo;
			case SurfacePresentModePolicy.AutoRelaxedFifo:
				if (haverelaxed)
					return PresentMode.FifoRelaxed;
				return havemailbox ? PresentMode.Mailbox : PresentMode.Fifo;
			case SurfacePresentModePolicy.AutoImmediate:
				if (haveimmediate)
					return PresentMode.Immediate;
				if (havemailbox)
					return PresentMode.Mailbox;
				return haverelaxed ? PresentMode.FifoRelaxed : PresentMode.Fifo;
			default:
				throw new UnreachableException();
			}
		} finally {
			device.API.SurfaceCapabilitiesFreeMembers(caps);
		}
	}

	private SurfaceConfiguration getSurfaceConfig(uint width, uint height, PresentMode presentMode) {
		SurfaceConfiguration cfg = default;
		cfg.Device = device.Device;
		cfg.Format = format;
		cfg.Usage = TextureUsage.RenderAttachment;
		cfg.Width = width;
		cfg.Height = height;
		cfg.PresentMode = presentMode;
		cfg.AlphaMode = CompositeAlphaMode.Auto;
		return cfg;
	}

	private void queryAndResize() {
		(uint w, uint h) = surfaceHost.GetDrawableSize();
		config = getSurfaceConfig(w, h, presentMode);
		fixed (SurfaceConfiguration *cfg = &config)
			device.API.SurfaceConfigure(surface, cfg);
		Width = w;
		Height = h;
	}

	private bool tryGetCurrentTex(out SurfaceTexture outTex) {
		SurfaceTexture tex;
		device.API.SurfaceGetCurrentTexture(surface, &tex);
		switch (tex.Status) {
		case SurfaceGetCurrentTextureStatus.Success:
			outTex = tex;
			return true;
		case SurfaceGetCurrentTextureStatus.Timeout:
			outTex = default;
			return false;
		case SurfaceGetCurrentTextureStatus.Outdated:
			queryAndResize();
			device.API.SurfaceGetCurrentTexture(surface, &tex);
			outTex = tex;
			return tex.Status == SurfaceGetCurrentTextureStatus.Success;
		case SurfaceGetCurrentTextureStatus.Lost:
			device.API.SurfaceRelease(surface);
			SurfaceDescriptorContainer sdc;
			surfaceHost.CreateSurfaceDesc(&sdc);
			surface = device.API.InstanceCreateSurface(device.Instance, &sdc.Desc);
			format = getSurfaceFormat();
			presentMode = getSurfacePresentMode();
			queryAndResize();
			device.API.SurfaceGetCurrentTexture(surface, &tex);
			outTex = tex;
			return tex.Status == SurfaceGetCurrentTextureStatus.Success;
		case SurfaceGetCurrentTextureStatus.DeviceLost:
			throw new WebGPUException("SurfaceGetCurrentTexture", "got DeviceLost, bailing out");
		case SurfaceGetCurrentTextureStatus.OutOfMemory:
			throw new OutOfMemoryException("WebGPU: SurfaceGetCurrentTexture out of memory");
		default:
			throw new WebGPUException("SurfaceGetCurrentTexture", tex.Status.ToString());
		}
	}

	public void Resized() {
		ObjectDisposedException.ThrowIf(disposed, this);
		queryAndResize();
	}

	public bool TryBeginFrame([NotNullWhen(true)] out RenderFrame? frame) {
		ObjectDisposedException.ThrowIf(disposed, this);
		frame = null;
		if (!tryGetCurrentTex(out SurfaceTexture currTex))
			return false;

		TextureViewDescriptor tvdesc = new TextureViewDescriptor {
			Format = format,
			Dimension = TextureViewDimension.Dimension2D,
			BaseMipLevel = 0,
			MipLevelCount = 1,
			BaseArrayLayer = 0,
			ArrayLayerCount = 1,
			Aspect = TextureAspect.All
		};
		TextureView *backbufferView = device.API.TextureCreateView(currTex.Texture, &tvdesc);
		if (backbufferView == null) {
			device.API.TextureRelease(currTex.Texture);
			throw new WebGPUException("TextureCreateView", "WebGPU call returned null");
		}
		CommandEncoderDescriptor encDesc = default;
		CommandEncoder *enc = device.API.DeviceCreateCommandEncoder(device.Device, &encDesc);
		if (enc is null) {
			device.API.TextureViewRelease(backbufferView);
			device.API.TextureRelease(currTex.Texture);
			throw new WebGPUException("DeviceCreateCommandEncoder", "WebGPU call returned null");
		}
		frame = new RenderFrame(device, currTex, backbufferView, enc, Present, Width, Height, Format);
		return true;
	}

	internal void Present() => device.API.SurfacePresent(surface);

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;

		if (surface is not null) device.API.SurfaceRelease(surface);
	}
}
