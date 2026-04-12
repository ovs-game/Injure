// SPDX-License-Identifier: MIT

using System;
using Hexa.NET.SDL2;
using WebGPU;

using Injure.Rendering;

namespace Injure.SDLUtil;

public sealed unsafe partial class SDLSurfaceHost(SDLWindow *win, void *metalLayer) : ISurfaceHost {
	private readonly SDLWindow *win = win;
	private readonly void *metalLayer = metalLayer;

	public void CreateSurfaceDesc(WGPUSurfaceDescriptorContainer *container) {
		ArgumentNullException.ThrowIfNull(container);
		if (win is null)
			throw new InternalStateException("this SDLSurfaceHost's Window is null");

		SDLSysWMInfo wm = default;
		SDL.GetVersion(&wm.Version);
		if (SDL.GetWindowWMInfo(win, &wm) != (SDLBool)1)
			throw new InvalidOperationException($"SDL_GetWindowWMInfo failed: {SDL.GetErrorS()}");

		if (wm.Subsystem == SdlSyswmType.Cocoa && metalLayer is null)
			throw new InternalStateException("this SDLSurfaceHost's Metal layer is null and we need it");
		switch (wm.Subsystem) {
		case SdlSyswmType.Windows: getWindows(&wm, container); break;
		case SdlSyswmType.Cocoa: getCocoa(metalLayer, container); break;
		case SdlSyswmType.X11: getX11(&wm, container); break;
		case SdlSyswmType.Wayland: getWayland(&wm, container); break;
		default: throw new PlatformNotSupportedException($"unsupported SDL WM subsystem '{wm.Subsystem}'");
		};
	}

	public (uint Width, uint Height) GetDrawableSize() {
		int w, h;
		SDL.GetWindowSizeInPixels(win, &w, &h);
		if (w < 0 || h < 0)
			throw new InvalidOperationException("SDL_GetWindowSizeInPixels returned negative size");
		return ((uint)w, (uint)h);
	}

	private static void getWindows(SDLSysWMInfo *wm, WGPUSurfaceDescriptorContainer *container) {
		void *hwnd = (void *)wm->Info.Win.Window;
		void *hinstance = (void *)wm->Info.Win.HInstance;

		container->WindowsHWND = new WGPUSurfaceSourceWindowsHWND {
			chain = new WGPUChainedStruct {
				sType = WGPUSType.SurfaceSourceWindowsHWND,
				next = null
			},
			hwnd = hwnd,
			hinstance = hinstance
		};
		container->Desc = new WGPUSurfaceDescriptor {
			nextInChain = &container->WindowsHWND.chain
		};
	}

	private static void getCocoa(void *metalLayer, WGPUSurfaceDescriptorContainer *container) {
		container->MetalLayer = new WGPUSurfaceSourceMetalLayer {
			chain = new WGPUChainedStruct {
				sType = WGPUSType.SurfaceSourceMetalLayer,
				next = null
			},
			layer = metalLayer
		};
		container->Desc = new WGPUSurfaceDescriptor {
			nextInChain = &container->MetalLayer.chain
		};
	}

	private static void getX11(SDLSysWMInfo *wm, WGPUSurfaceDescriptorContainer *container) {
		// xlib windows are 32-bit resource IDs, it's 64-bit here because
		// C unsigned long is 64-bit on LP64 ABIs
		void *dpy = (void *)wm->Info.X11.Display;
		ulong win = (ulong)wm->Info.X11.Window;

		container->XlibWindow = new WGPUSurfaceSourceXlibWindow {
			chain = new WGPUChainedStruct {
				sType = WGPUSType.SurfaceSourceXlibWindow,
				next = null
			},
			display = dpy,
			window = win
		};
		container->Desc = new WGPUSurfaceDescriptor {
			nextInChain = &container->XlibWindow.chain
		};
	}

	private static void getWayland(SDLSysWMInfo *wm, WGPUSurfaceDescriptorContainer *container) {
		void *wl_display = (void *)wm->Info.Wayland.Display;
		void *wl_surface = (void *)wm->Info.Wayland.Surface;

		container->WaylandSurface = new WGPUSurfaceSourceWaylandSurface {
			chain = new WGPUChainedStruct {
				sType = WGPUSType.SurfaceSourceWaylandSurface,
				next = null
			},
			display = wl_display,
			surface = wl_surface
		};
		container->Desc = new WGPUSurfaceDescriptor {
			nextInChain = &container->WaylandSurface.chain
		};
	}
}
