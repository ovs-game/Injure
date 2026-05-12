// SPDX-License-Identifier: MIT

using System;
using Hexa.NET.SDL3;
using WebGPU;

using Injure.Rendering;

namespace Injure.Core;

public sealed unsafe partial class SDLSurfaceHost(SDLWindow *win, void *metalLayer) : ISurfaceHost {
	private readonly SDLWindow *win = win;
	private readonly void *metalLayer = metalLayer;

	private const string DrvCocoa = "cocoa";
	private const string DrvWayland = "wayland";
	private const string DrvWindows = "windows";
	private const string DrvX11 = "x11";

	public void CreateSurfaceDesc(WGPUSurfaceDescriptorContainer *container) {
		ArgumentNullException.ThrowIfNull(container);
		if (win is null)
			throw new InternalStateException("this SDLSurfaceHost's Window is null");

		uint props = SDL.GetWindowProperties(win);
		string drv = SDL.GetCurrentVideoDriverS();
		switch (drv) {
		case DrvCocoa:
			if (metalLayer is null)
				throw new InternalStateException("this SDLSurfaceHost's Metal layer is null and we need it");
			getCocoa(metalLayer, container);
			break;
		case DrvWayland:
			getWayland(props, container);
			break;
		case DrvWindows:
			getWindows(props, container);
			break;
		case DrvX11:
			getX11(props, container);
			break;
		default:
			throw new PlatformNotSupportedException($"unsupported SDL videodriver '{drv}'");
		}
	}

	public (uint Width, uint Height) GetDrawableSize() {
		int w, h;
		SDL.GetWindowSizeInPixels(win, &w, &h);
		if (w < 0 || h < 0)
			throw new InvalidOperationException("SDL_GetWindowSizeInPixels returned negative size");
		return ((uint)w, (uint)h);
	}

	private static void getWindows(uint props, WGPUSurfaceDescriptorContainer *container) {
		void *hwnd = SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_WIN32_HWND_POINTER, null);
		void *hinstance = SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_WIN32_INSTANCE_POINTER, null);

		container->WindowsHWND = new WGPUSurfaceSourceWindowsHWND {
			chain = new WGPUChainedStruct {
				sType = WGPUSType.SurfaceSourceWindowsHWND,
				next = null,
			},
			hwnd = hwnd,
			hinstance = hinstance,
		};
		container->Desc = new WGPUSurfaceDescriptor {
			nextInChain = &container->WindowsHWND.chain,
		};
	}

	private static void getCocoa(void *metalLayer, WGPUSurfaceDescriptorContainer *container) {
		container->MetalLayer = new WGPUSurfaceSourceMetalLayer {
			chain = new WGPUChainedStruct {
				sType = WGPUSType.SurfaceSourceMetalLayer,
				next = null,
			},
			layer = metalLayer,
		};
		container->Desc = new WGPUSurfaceDescriptor {
			nextInChain = &container->MetalLayer.chain,
		};
	}

	private static void getX11(uint props, WGPUSurfaceDescriptorContainer *container) {
		// xlib windows are 32-bit resource IDs, it's 64-bit here because
		// C unsigned long is 64-bit on LP64 ABIs
		void *dpy = SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_X11_DISPLAY_POINTER, null);
		ulong win = (ulong)SDL.GetNumberProperty(props, SDL.SDL_PROP_WINDOW_X11_WINDOW_NUMBER, 0);

		container->XlibWindow = new WGPUSurfaceSourceXlibWindow {
			chain = new WGPUChainedStruct {
				sType = WGPUSType.SurfaceSourceXlibWindow,
				next = null,
			},
			display = dpy,
			window = win,
		};
		container->Desc = new WGPUSurfaceDescriptor {
			nextInChain = &container->XlibWindow.chain,
		};
	}

	private static void getWayland(uint props, WGPUSurfaceDescriptorContainer *container) {
		void *wl_display = SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_WAYLAND_DISPLAY_POINTER, null);
		void *wl_surface = SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_WAYLAND_SURFACE_POINTER, null);

		container->WaylandSurface = new WGPUSurfaceSourceWaylandSurface {
			chain = new WGPUChainedStruct {
				sType = WGPUSType.SurfaceSourceWaylandSurface,
				next = null,
			},
			display = wl_display,
			surface = wl_surface,
		};
		container->Desc = new WGPUSurfaceDescriptor {
			nextInChain = &container->WaylandSurface.chain,
		};
	}
}
