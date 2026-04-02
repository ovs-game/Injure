// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.Core.Native;
using Silk.NET.SDL;

using Injure.Timing;

namespace Injure.SDL;

public static unsafe class SDLOwner {
	public static readonly Sdl SDL = Sdl.GetApi();
	public static Window *Window { get; private set; }
	public static void *AppleMetalView { get; private set; } = null;
	public static void *AppleMetalLayer { get; private set; } = null;

	public static SDLRenderSurfaceSource? RenderSurfaceSource { get; private set; }

	internal static PerfTick PerfTickFrequency { get; private set; }
	private static bool inited = false;

	[MemberNotNull(nameof(Window), nameof(RenderSurfaceSource))]
	public static void InitSDL(string title, int x, int y, int w, int h, WindowFlags flags) {
		if (SDL.Init(Sdl.InitVideo | Sdl.InitEvents) < 0)
			throw new InvalidOperationException($"SDL_Init: {SDL.GetErrorS()}");

		if (OperatingSystem.IsMacOS())
			flags |= WindowFlags.Metal;

		byte *p = (byte *)SilkMarshal.StringToPtr(title, NativeStringEncoding.UTF8);
		try {
			Window = SDL.CreateWindow(p, x, y, w, h, (uint)flags);
		} finally {
			SilkMarshal.Free((IntPtr)p);
		}
		if (Window is null) {
			SDL.Quit();
			throw new InvalidOperationException($"SDL_CreateWindow: {SDL.GetErrorS()}");
		}

		if (OperatingSystem.IsMacOS()) {
			AppleMetalView = SDL.MetalCreateView(Window);
			if (AppleMetalView is null) {
				SDL.DestroyWindow(Window);
				SDL.Quit();
				throw new InvalidOperationException("SDL_Metal_CreateView returned null");
			}

			AppleMetalLayer = SDL.MetalGetLayer(AppleMetalView);
			if (AppleMetalLayer is null) {
				SDL.MetalDestroyView(AppleMetalView);
				SDL.DestroyWindow(Window);
				SDL.Quit();
				throw new InvalidOperationException("SDL_Metal_GetLayer returned null");
			}
		}

		RenderSurfaceSource = new SDLRenderSurfaceSource(Window, AppleMetalLayer);

		PerfTickFrequency = (PerfTick)SDL.GetPerformanceFrequency();
		inited = true;
	}
	
	public static void ShutdownSDL() {
		if (inited) {
			RenderSurfaceSource = null;
			if (AppleMetalView is not null) {
				SDL.MetalDestroyView(AppleMetalView);
				AppleMetalLayer = null;
				AppleMetalView = null;
			}
			SDL.DestroyWindow(Window);
			Window = null;
			SDL.Quit();
		}
	}

	internal static PerfTick PerfTickGetCurrent() {
		if (!inited)
			throw new InvalidOperationException("SDLOwner.Init() not called yet");
		return (PerfTick)SDL.GetPerformanceCounter();
	}
}
