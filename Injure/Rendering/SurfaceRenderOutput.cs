// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using WebGPU;
using static WebGPU.WebGPU;

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

/// <summary>
/// Policy for selecting a surface present mode in <see cref="SurfaceRenderOutput"/>.
/// </summary>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct SurfacePresentModePolicy {
	public enum Case {
		/// <summary>
		/// Prefer <see cref="PresentMode.Mailbox"/>, fall back to <see cref="PresentMode.Fifo"/>
		/// if not present.
		/// </summary>
		/// <remarks>
		/// Tear-free.
		/// </remarks>
		AutoMailbox = 1,

		/// <summary>
		/// Prefer <see cref="PresentMode.FifoRelaxed"/>, fall back to <see cref="PresentMode.Mailbox"/>
		/// and then <see cref="PresentMode.Fifo"/> if not present.
		/// </summary>
		/// <remarks>
		/// Normally tear-free; may tear if a frame remains on the frontbuffer for more than one vblank.
		/// </remarks>
		AutoFifoRelaxed,

		/// <summary>
		/// Prefer <see cref="PresentMode.Immediate"/>, fall back to <see cref="PresentMode.Mailbox"/>,
		/// then <see cref="PresentMode.FifoRelaxed"/>, and finally <see cref="PresentMode.Fifo"/>
		/// if not present.
		/// </summary>
		/// <remarks>
		/// May tear. Lowest latency.
		/// </remarks>
		AutoImmediate
	}
}

public sealed unsafe class SurfaceRenderOutput : IRenderOutput {
	private enum AcquireStatus {
		Acquired,
		AcquiredNeedsReconfigure,
		SkipFrame,
		DeviceLost
	}

	private readonly WebGPUDevice device;
	private readonly ISurfaceHost surfaceHost;
	private SurfacePresentModePolicy presentPolicy;

	private WGPUSurface surface;
	private WGPUTextureFormat format;
	private WGPUPresentMode presentMode;
	private WGPUSurfaceConfiguration config;
	private bool needReconfigure = false;
	private bool disposed = false;

	public uint Width { get { ObjectDisposedException.ThrowIf(disposed, this); return field; } private set; }
	public uint Height { get { ObjectDisposedException.ThrowIf(disposed, this); return field; } private set; }
	public TextureFormat Format { get { ObjectDisposedException.ThrowIf(disposed, this); return format.FromWebGPUType(); } }

	public SurfaceRenderOutput(WebGPUDevice device, ISurfaceHost surfaceHost, SurfacePresentModePolicy presentPolicy) {
		this.device = device;
		this.surfaceHost = surfaceHost;
		this.presentPolicy = presentPolicy;

		WGPUSurfaceDescriptorContainer sdc;
		surfaceHost.CreateSurfaceDesc(&sdc);
		surface = wgpuInstanceCreateSurface(device.Instance, &sdc.Desc);
		format = getSurfaceFormat();
		presentMode = getSurfacePresentMode();
		reconfigure();
	}

	public void SetPresentModePolicy(SurfacePresentModePolicy policy) {
		ObjectDisposedException.ThrowIf(disposed, this);
		presentPolicy = policy;
		presentMode = getSurfacePresentMode();
		needReconfigure = true;
	}

	private WGPUTextureFormat getSurfaceFormat() {
		WGPUSurfaceCapabilities caps;
		wgpuSurfaceGetCapabilities(surface, device.Adapter, &caps);
		try {
			if (caps.formatCount == 0)
				throw new WebGPUException("SurfaceGetCapabilities", "surface doesn't report any supported formats");
			// wgpu says the first format is the most preferred one
			return caps.formats[0];
		} finally {
			wgpuSurfaceCapabilitiesFreeMembers(caps);
		}
	}

	private WGPUPresentMode getSurfacePresentMode() {
		WGPUSurfaceCapabilities caps;
		wgpuSurfaceGetCapabilities(surface, device.Adapter, &caps);
		try {
			if (caps.presentModeCount == 0)
				throw new WebGPUException("SurfaceGetCapabilities", "surface doesn't report any supported present modes");
			ReadOnlySpan<WGPUPresentMode> modes = new(caps.presentModes, (int)caps.presentModeCount);
			bool haverelaxed = modes.Contains(WGPUPresentMode.FifoRelaxed);
			bool havemailbox = modes.Contains(WGPUPresentMode.Mailbox);
			bool haveimmediate = modes.Contains(WGPUPresentMode.Immediate);
			switch (presentPolicy.Tag) {
			case SurfacePresentModePolicy.Case.AutoMailbox:
				return havemailbox ? WGPUPresentMode.Mailbox : WGPUPresentMode.Fifo;
			case SurfacePresentModePolicy.Case.AutoFifoRelaxed:
				if (haverelaxed)
					return WGPUPresentMode.FifoRelaxed;
				return havemailbox ? WGPUPresentMode.Mailbox : WGPUPresentMode.Fifo;
			case SurfacePresentModePolicy.Case.AutoImmediate:
				if (haveimmediate)
					return WGPUPresentMode.Immediate;
				if (havemailbox)
					return WGPUPresentMode.Mailbox;
				return haverelaxed ? WGPUPresentMode.FifoRelaxed : WGPUPresentMode.Fifo;
			default:
				throw new UnreachableException();
			}
		} finally {
			wgpuSurfaceCapabilitiesFreeMembers(caps);
		}
	}

	private WGPUSurfaceConfiguration getSurfaceConfig(uint width, uint height, WGPUPresentMode presentMode) {
		return new WGPUSurfaceConfiguration {
			device = device.Device,
			format = format,
			usage = WGPUTextureUsage.RenderAttachment,
			width = width,
			height = height,
			presentMode = presentMode,
			alphaMode = WGPUCompositeAlphaMode.Auto
		};
	}

	private void reconfigure() {
		(uint w, uint h) = surfaceHost.GetDrawableSize();
		config = getSurfaceConfig(w, h, presentMode);
		fixed (WGPUSurfaceConfiguration *cfg = &config)
			wgpuSurfaceConfigure(surface, cfg);
		Width = w;
		Height = h;
	}

	private AcquireStatus acquire(out WGPUSurfaceTexture outTex) {
		static AcquireStatus from(WGPUSurfaceGetCurrentTextureStatus st) {
			return st switch {
				WGPUSurfaceGetCurrentTextureStatus.SuccessOptimal => AcquireStatus.Acquired,
				WGPUSurfaceGetCurrentTextureStatus.SuccessSuboptimal => AcquireStatus.AcquiredNeedsReconfigure,
				WGPUSurfaceGetCurrentTextureStatus.Timeout => AcquireStatus.SkipFrame,
				WGPUSurfaceGetCurrentTextureStatus.Outdated => AcquireStatus.SkipFrame,
				WGPUSurfaceGetCurrentTextureStatus.Lost => AcquireStatus.SkipFrame,
				WGPUSurfaceGetCurrentTextureStatus.DeviceLost => AcquireStatus.DeviceLost,
				WGPUSurfaceGetCurrentTextureStatus.OutOfMemory => throw new OutOfMemoryException("WebGPU: wgpuSurfaceGetCurrentTexture: out of memory"),
				_ => throw new WebGPUException("wgpuSurfaceGetCurrentTexture", st.ToString())
			};
		}

		WGPUSurfaceTexture tex = default;
		wgpuSurfaceGetCurrentTexture(surface, &tex);
		switch (tex.status) {
		case WGPUSurfaceGetCurrentTextureStatus.SuccessOptimal:
			outTex = tex;
			return AcquireStatus.Acquired;
		case WGPUSurfaceGetCurrentTextureStatus.SuccessSuboptimal:
			outTex = tex;
			return AcquireStatus.AcquiredNeedsReconfigure;
		case WGPUSurfaceGetCurrentTextureStatus.Timeout:
			outTex = default;
			return AcquireStatus.SkipFrame;
		case WGPUSurfaceGetCurrentTextureStatus.Outdated:
			reconfigure();
			wgpuSurfaceGetCurrentTexture(surface, &tex);
			outTex = tex;
			return from(tex.status);
		case WGPUSurfaceGetCurrentTextureStatus.Lost:
			wgpuSurfaceRelease(surface);
			WGPUSurfaceDescriptorContainer sdc;
			surfaceHost.CreateSurfaceDesc(&sdc);
			surface = wgpuInstanceCreateSurface(device.Instance, &sdc.Desc);
			format = getSurfaceFormat();
			presentMode = getSurfacePresentMode();
			reconfigure();
			wgpuSurfaceGetCurrentTexture(surface, &tex);
			outTex = tex;
			return from(tex.status);
		case WGPUSurfaceGetCurrentTextureStatus.DeviceLost:
			outTex = default;
			return AcquireStatus.DeviceLost;
		case WGPUSurfaceGetCurrentTextureStatus.OutOfMemory:
			throw new OutOfMemoryException("WebGPU: wgpuSurfaceGetCurrentTexture out of memory");
		default:
			throw new WebGPUException("wgpuSurfaceGetCurrentTexture", tex.status.ToString());
		}
	}

	public void Resized() {
		ObjectDisposedException.ThrowIf(disposed, this);
		reconfigure();
	}

	public bool TryBeginFrame([NotNullWhen(true)] out RenderFrame? frame) {
		ObjectDisposedException.ThrowIf(disposed, this);
		frame = null;

		if (needReconfigure)
			reconfigure();

		AcquireStatus st = acquire(out WGPUSurfaceTexture currTex);
		if (!(st is AcquireStatus.Acquired or AcquireStatus.AcquiredNeedsReconfigure)) {
			if (st == AcquireStatus.DeviceLost) {
				device.NotifyLost(new DeviceLostInfo(DeviceLossInfoKind.Provisional,
					DeviceLossEventReason.SurfaceAcquireDeviceLost, "got DeviceLost while trying to begin a render frame"));
				device.TripLostException();
			}
			return false;
		}
		if (st == AcquireStatus.AcquiredNeedsReconfigure)
			needReconfigure = true;

		WGPUTextureViewDescriptor tvdesc = new() {
			format = format,
			dimension = WGPUTextureViewDimension._2D,
			aspect = WGPUTextureAspect.All,
			baseMipLevel = 0,
			mipLevelCount = 1,
			baseArrayLayer = 0,
			arrayLayerCount = 1
		};
		WGPUTextureView backbufferView = wgpuTextureCreateView(currTex.texture, &tvdesc);
		if (backbufferView.IsNull) {
			wgpuTextureRelease(currTex.texture);
			throw new WebGPUException("wgpuTextureCreateView", "WebGPU call returned null");
		}
		WGPUCommandEncoderDescriptor encDesc = default;
		WGPUCommandEncoder enc = wgpuDeviceCreateCommandEncoder(device.Device, &encDesc);
		if (enc.IsNull) {
			wgpuTextureViewRelease(backbufferView);
			wgpuTextureRelease(currTex.texture);
			throw new WebGPUException("wgpuDeviceCreateCommandEncoder", "WebGPU call returned null");
		}
		GPUTextureView v = new(backbufferView, tvdesc.format.FromWebGPUType(), tvdesc.dimension.FromWebGPUType(),
			tvdesc.aspect.FromWebGPUType(), config.usage.FromWebGPUType(), tvdesc.baseMipLevel, tvdesc.mipLevelCount,
			tvdesc.baseArrayLayer, tvdesc.arrayLayerCount, config.width, config.height, 1, 1);
		frame = new RenderFrame(device, currTex, v, enc, Present);
		return true;
	}

	internal void Present() => wgpuSurfacePresent(surface);

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		if (surface.IsNotNull)
			wgpuSurfaceRelease(surface);
	}
}
