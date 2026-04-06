// SPDX-License-Identifier: MIT

using System;
using Hexa.NET.SDL2;
using Silk.NET.WebGPU;

using Injure.Rendering;

namespace Injure.SDLUtil;

public sealed unsafe partial class SDLSurfaceHost(SDLWindow *win, void *metalLayer) : ISurfaceHost {
	private readonly SDLWindow *win = win;
	private readonly void *metalLayer = metalLayer;

	public void CreateSurfaceDesc(SurfaceDescriptorContainer *container) {
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

	private static void getWindows(SDLSysWMInfo *wm, SurfaceDescriptorContainer *container) {
		void *hwnd = (void *)wm->Info.Win.Window;
		void *hinstance = (void *)wm->Info.Win.HInstance;

		container->WindowsHWND = new SurfaceDescriptorFromWindowsHWND {
			Chain = new ChainedStruct {
				SType = SType.SurfaceDescriptorFromWindowsHwnd,
				Next = null
			},
			Hwnd = hwnd,
			Hinstance = hinstance
		};
		container->Desc = new SurfaceDescriptor {
			NextInChain = (ChainedStruct *)&container->WindowsHWND
		};
	}

	private static void getCocoa(void *metalLayer, SurfaceDescriptorContainer *container) {
		container->MetalLayer = new SurfaceDescriptorFromMetalLayer {
			Chain = new ChainedStruct {
				SType = SType.SurfaceDescriptorFromMetalLayer,
				Next = null
			},
			Layer = metalLayer
		};
		container->Desc = new SurfaceDescriptor {
			NextInChain = (ChainedStruct *)&container->MetalLayer
		};
	}

	private static void getX11(SDLSysWMInfo *wm, SurfaceDescriptorContainer *container) {
		void *dpy = (void *)wm->Info.X11.Display;
		void *win = (void *)wm->Info.X11.Window;

		container->XlibWindow = new SurfaceDescriptorFromXlibWindow {
			Chain = new ChainedStruct {
				SType = SType.SurfaceDescriptorFromXlibWindow,
				Next = null
			},
			Display = dpy,
			Window = (ulong)win // i have No fucking clue why this needs the cast when everything else takes void *
		};
		container->Desc = new SurfaceDescriptor {
			NextInChain = (ChainedStruct *)&container->XlibWindow
		};
	}

	private static void getWayland(SDLSysWMInfo *wm, SurfaceDescriptorContainer *container) {
		void *wl_display = (void *)wm->Info.Wayland.Display;
		void *wl_surface = (void *)wm->Info.Wayland.Surface;

		container->WaylandSurface = new SurfaceDescriptorFromWaylandSurface {
			Chain = new ChainedStruct {
				SType = SType.SurfaceDescriptorFromWaylandSurface,
				Next = null
			},
			Display = wl_display,
			Surface = wl_surface
		};
		container->Desc = new SurfaceDescriptor {
			NextInChain = (ChainedStruct *)&container->WaylandSurface
		};
	}
}
