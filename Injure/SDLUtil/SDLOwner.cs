// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using Hexa.NET.SDL2;

using Injure.Timing;

namespace Injure.SDLUtil;

public static unsafe class SDLOwner {
	public static SDLWindow *Window { get; private set; }
	public static void *AppleMetalView { get; private set; } = null;
	public static void *AppleMetalLayer { get; private set; } = null;

	public static SDLSurfaceHost? SurfaceHost { get; private set; }

	internal static PerfTick PerfTickFrequency { get; private set; }
	private static bool inited = false;

	[MemberNotNull(nameof(Window), nameof(SurfaceHost))]
	public static void InitSDL(string title, int x, int y, int w, int h, SDLWindowFlags flags) {
		if (SDL.Init(SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_EVENTS) < 0)
			throw new InvalidOperationException($"SDL_Init: {SDL.GetErrorS()}");

		if (OperatingSystem.IsMacOS())
			flags |= SDLWindowFlags.Metal;

		Window = SDL.CreateWindow(title, x, y, w, h, (uint)flags);
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

		SurfaceHost = new SDLSurfaceHost(Window, AppleMetalLayer);

		PerfTickFrequency = (PerfTick)SDL.GetPerformanceFrequency();
		inited = true;
	}
	
	public static void ShutdownSDL() {
		if (inited) {
			SurfaceHost = null;
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
			throw new InvalidOperationException("SDLOwner.InitSDL() not called yet");
		return (PerfTick)SDL.GetPerformanceCounter();
	}
}
