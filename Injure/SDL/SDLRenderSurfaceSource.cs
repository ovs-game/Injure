// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using Silk.NET.SDL;
using Silk.NET.WebGPU;

using Injure.Rendering;

using Version = Silk.NET.SDL.Version;

namespace Injure.SDL;

public sealed unsafe partial class SDLRenderSurfaceSource(Window *win, void *metalLayer) : IRenderSurfaceSource {
	[LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
	private static partial IntPtr GetModuleHandleW(string? lpModuleName);

	private readonly Window *win = win;
	private readonly void *metalLayer = metalLayer;

	public void CreateSurfaceDesc(SurfaceDescriptorContainer *container) {
		if (win is null)
			throw new InternalStateException("this SDLRenderSurfaceSource's Window is null");

		Version ver;
		SDLOwner.SDL.GetVersion(&ver);
		SysWMInfo wm = new SysWMInfo { Version = ver };
		if (!SDLOwner.SDL.GetWindowWMInfo(win, &wm))
			throw new InvalidOperationException($"SDL_GetWindowWMInfo failed: {SDLOwner.SDL.GetErrorS()}");

		if (wm.Subsystem == SysWMType.Cocoa && metalLayer is null)
			throw new InternalStateException("this SDLRenderSurfaceSource's Metal layer is null and we need it");
		switch (wm.Subsystem) {
		case SysWMType.Windows: getWindows(&wm, container); break;
		case SysWMType.Cocoa: getCocoa(metalLayer, container); break;
		case SysWMType.X11: getX11(&wm, container); break;
		case SysWMType.Wayland: getWayland(&wm, container); break;
		default: throw new PlatformNotSupportedException($"unsupported SDL WM subsystem '{wm.Subsystem}'");
		};
	}

	public (uint Width, uint Height) GetDrawableSize() {
		int w, h;
		SDLOwner.SDL.GetWindowSizeInPixels(win, &w, &h);
		if (w < 0 || h < 0)
			throw new InvalidOperationException("SDL_GetWindowSizeInPixels returned negative size");
		return ((uint)w, (uint)h);
	}

	private static void getWindows(SysWMInfo *wm, SurfaceDescriptorContainer *container) {
		IntPtr hwnd = wm->Info.Win.Hwnd;
		IntPtr hinstance = GetModuleHandleW(null);

		container->WindowsHWND = new SurfaceDescriptorFromWindowsHWND {
			Chain = new ChainedStruct {
				SType = SType.SurfaceDescriptorFromWindowsHwnd,
				Next = null
			},
			Hwnd = (void *)hwnd,
			Hinstance = (void *)hinstance
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

	private static void getX11(SysWMInfo *wm, SurfaceDescriptorContainer *container) {
		void *dpy = wm->Info.X11.Display;
		void *win = wm->Info.X11.Window;

		container->XlibWindow = new SurfaceDescriptorFromXlibWindow {
			Chain = new ChainedStruct {
				SType = SType.SurfaceDescriptorFromXlibWindow,
				Next = null
			},
			Display = dpy,
			Window = (UIntPtr)win // i have No fucking clue why this needs the cast when everything else takes void *
		};
		container->Desc = new SurfaceDescriptor {
			NextInChain = (ChainedStruct *)&container->XlibWindow
		};
	}

	private static void getWayland(SysWMInfo *wm, SurfaceDescriptorContainer *container) {
		void *wl_display = wm->Info.Wayland.Display;
		void *wl_surface = wm->Info.Wayland.Surface;

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
