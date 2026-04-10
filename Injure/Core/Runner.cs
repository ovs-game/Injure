// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading.Tasks;
using Hexa.NET.SDL2;

using Injure.Assets;
using Injure.Assets.Builtin;
using Injure.Audio;
using Injure.Graphics;
using Injure.Graphics.Text;
using Injure.Input;
using Injure.Rendering;
using Injure.SDLUtil;
using Injure.Timing;

using Thread = System.Threading.Thread;

namespace Injure.Core;

public static unsafe class Runner {
	public static readonly CanvasParams BaseCanvasParams = new CanvasParams(
		Target: CanvasTarget.Primary,
		ColorAttachmentOps: ColorAttachmentOps.Clear(Color32.Black),
		Scissor: CanvasScissor.None,
		Transform: Matrix3x2.Identity,
		OutputState: CanvasOutputStates.Alpha,
		Material: CanvasMaterials.Color
	);

	[AllowNull] private static WebGPUDevice gpuDevice;
	[AllowNull] private static SurfaceRenderOutput sfOutput;
	[AllowNull] private static ViewGlobals viewGlobals;

	[AllowNull] private static GameServices services;
	[AllowNull] private static CanvasSharedResources canvasResources;
	[AllowNull] private static Queue<RawInputEvent> inputQueue;
	private static bool running = false;

	private static void handleEvent(SDLEvent *ev, IGame game, ref bool cont) {
		switch ((SDLEventType)ev->Type) {
		case SDLEventType.Quit:
			cont = false;
			break;
		case SDLEventType.Windowevent:
			switch ((SDLWindowEventID)ev->Window.Event) {
			case SDLWindowEventID.SizeChanged:
				// hand over logical window size, not px-drawable size to the game
				// since that's probably more fitting here, the renderer gets
				// px-drawable size internally
				sfOutput.Resized();
				viewGlobals.Update(sfOutput.Width, sfOutput.Height);
				game.OnHostEvent(new HostEvent(HostEventKind.Resized, (uint)ev->Window.Data1, (uint)ev->Window.Data2));
				break;
			case SDLWindowEventID.Minimized:
				game.OnHostEvent(new HostEvent(HostEventKind.Minimized));
				break;
			case SDLWindowEventID.Maximized:
				game.OnHostEvent(new HostEvent(HostEventKind.Maximized));
				break;
			case SDLWindowEventID.Restored:
				game.OnHostEvent(new HostEvent(HostEventKind.Restored));
				break;
			case SDLWindowEventID.FocusGained:
				game.OnHostEvent(new HostEvent(HostEventKind.FocusGained));
				break;
			case SDLWindowEventID.FocusLost:
				game.OnHostEvent(new HostEvent(HostEventKind.FocusLost));
				break;
			}
			break;
		case SDLEventType.Keydown:
		case SDLEventType.Keyup:
			if (ev->Key.Repeat == 1)
				break;
			PerfTick timestamp = PerfTick.GetCurrent();
			RawInputID id = new RawInputID(InputDeviceType.Keyboard, -1, (int)ev->Key.Keysym.Scancode);
			inputQueue.Enqueue(new RawInputEvent(id, (SDLEventType)ev->Type == SDLEventType.Keydown ? EdgeType.Press : EdgeType.Release, timestamp));
			break;
		}
	}

	private static void render(IGame game) {
		if (!sfOutput.TryBeginFrame(out RenderFrame? frame))
			return;
		using (frame) {
			using (Canvas cv = new Canvas(gpuDevice, viewGlobals, frame, canvasResources, in BaseCanvasParams))
				game.Render(cv);
			frame.SubmitAndPresent();
		}
	}

	private static void initSDLFrom(in GameWindowConfig conf) {
		SDLWindowFlags flags = 0;
		if (!conf.StartVisible) flags |= SDLWindowFlags.Hidden;
		if (conf.Resizable) flags |= SDLWindowFlags.Resizable;
		if (conf.Borderless) flags |= SDLWindowFlags.Borderless;
		if (conf.AllowHighDPI) flags |= SDLWindowFlags.AllowHighdpi;
		switch (conf.Mode) {
		case WindowMode.BorderlessFullscreen: flags |= SDLWindowFlags.Borderless | SDLWindowFlags.FullscreenDesktop; break;
		case WindowMode.ExclusiveFullscreen: flags |= SDLWindowFlags.Fullscreen; break;
		}
		switch (conf.StartState) {
		case WindowState.Minimized: flags |= SDLWindowFlags.Minimized; break;
		case WindowState.Maximized: flags |= SDLWindowFlags.Maximized; break;
		}
		int x, y;
		switch (conf.StartPositioning) {
		case WindowPositioning.Undefined: x = y = unchecked((int)SDL.SDL_WINDOWPOS_UNDEFINED_MASK); break;
		case WindowPositioning.Centered: x = y = unchecked((int)SDL.SDL_WINDOWPOS_CENTERED_MASK); break;
		case WindowPositioning.Explicit: x = conf.StartX; y = conf.StartY; break;
		default: throw new UnreachableException(); // silence "use of unassigned local"
		}
		SDLOwner.InitSDL(conf.Title, x, y, conf.Width, conf.Height, flags);
	}

	public static void Run(IGame game, in GameConfig conf) {
		GameServicesConfig svconf = conf.Services;
		GameWindowConfig winconf = conf.Window;
		GameRenderConfig rconf = conf.Render;
		GameTimingConfig tmconf = conf.Timing;

		// validation and basic init
		if (tmconf.RenderMode == RenderTimingMode.Capped) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tmconf.TargetFPS);
		if (tmconf.LoopMode == LoopTimingMode.Wait) {
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tmconf.TargetLoopHz);
			if (tmconf.TargetFPS > tmconf.TargetLoopHz)
				throw new ArgumentException("TargetFPS cannot be higher than TargetLoopHz; if you want to go higher, increase TargetLoopHz");
		}

		if (running)
			throw new InvalidOperationException("an IGame instance is already running");
		running = true;

		double renderStep = tmconf.TargetFPS > 0.0 ? 1.0 / tmconf.TargetFPS : 0.0;
		double loopStep = tmconf.TargetLoopHz > 0.0 ? 1.0 / tmconf.TargetLoopHz : 0.0;
		PerfTick loopStepTicks = (PerfTick)(ulong)Math.Round(loopStep * (double)PerfTick.Frequency);

		PerfTick t1_5ms = (PerfTick)(ulong)((UInt128)PerfTick.Frequency.Value * 15 / 10000);
		PerfTick t0_5ms = (PerfTick)(ulong)((UInt128)PerfTick.Frequency.Value * 5 / 10000);
		PerfTick t0_1ms = (PerfTick)(ulong)((UInt128)PerfTick.Frequency.Value * 1 / 10000);

		// sdl/precisewait init
		initSDLFrom(in winconf);
		PreciseWait.Init();
		game.Loading(new LoadingContext(LoadingPhase.Start, redrawRequested: true));
		PerfTick loadingStartTick = PerfTick.GetCurrent();

		// webgpu bootstrap
		Task<WebGPUDevice> bootstrap = Task.Run(() => new WebGPUDevice());

		// basic loading-time event loop
		SDLEvent ev;
		double elapsed;
		bool cancelled = false;
		while (!bootstrap.IsCompleted) {
			if (!cancelled) {
				while (SDL.PollEvent(&ev) == 1) {
					switch ((SDLEventType)ev.Type) {
					case SDLEventType.Quit:
						SDL.HideWindow(SDLOwner.Window);
						cancelled = true;
						goto bootstrapCancelled;
					case SDLEventType.Windowevent:
						elapsed = (double)(PerfTick.GetCurrent() - loadingStartTick) / (double)PerfTick.Frequency;
						game.Loading(new LoadingContext(LoadingPhase.Tick, elapsed, redrawRequested: true));
						break;
					}
				}
				elapsed = (double)(PerfTick.GetCurrent() - loadingStartTick) / (double)PerfTick.Frequency;
				game.Loading(new LoadingContext(LoadingPhase.Tick, elapsed));
			}
bootstrapCancelled:
			SDL.Delay(10);
		}
		if (!bootstrap.IsCompletedSuccessfully)
			goto earlyquit;
		elapsed = (double)(PerfTick.GetCurrent() - loadingStartTick) / (double)PerfTick.Frequency;
		game.Loading(new LoadingContext(LoadingPhase.Finish, elapsed));

		// webgpu setup
		gpuDevice = bootstrap.Result;
		sfOutput = new SurfaceRenderOutput(gpuDevice, SDLOwner.SurfaceHost!, rconf.PresentMode switch {
			PresentMode.TearFree => SurfacePresentModePolicy.AutoMailbox,
			PresentMode.Adaptive => SurfacePresentModePolicy.AutoRelaxedFifo,
			PresentMode.LowLatency => SurfacePresentModePolicy.AutoImmediate,
			_ => throw new UnreachableException()
		});
		viewGlobals = new ViewGlobals(gpuDevice, sfOutput.Width, sfOutput.Height);

		// service init
		// TODO: the builtin source/resolver/creator registry should be moved out somewhere
		PerfTick budget = (PerfTick)Math.Min((ulong)loopStepTicks, (ulong)PerfTick.PeriodFromHz(1000.0));
		TickerScheduler sched = new TickerScheduler(new TickerSchedulerOptions(MaxBatchDuration: budget));
		EngineResourceStore eresources = new EngineResourceStore();
		eresources.RegisterSource(new EmbeddedEngineResourceSource(
			typeof(Runner).Assembly,
			new HashSet<EngineResourceID>([
				BuiltinShaders.Primitive2D.ResourceID,
				BuiltinShaders.Textured2DColor.ResourceID,
				BuiltinShaders.Textured2DRMask.ResourceID,
				BuiltinShaders.Textured2DSDF.ResourceID
			])
		));
		AssetStore? assets = null;
		AssetThreadContext? assetCtx = null;
		if (svconf.Assets) {
			assets = new AssetStore();
			assets.RegisterResolver(ModUtils.Info.OwnerID, new Texture2DJsonAssetResolver(), "Texture2DJsonAssetResolver");
			assets.RegisterResolver(ModUtils.Info.OwnerID, new Texture2DImageAssetResolver(), "Texture2DImageAssetResolver");
			assets.RegisterCreator(ModUtils.Info.OwnerID, new Texture2DAssetCreator(gpuDevice), "Texture2DAssetCreator");
			assetCtx = assets.AttachCurrentThread();
		}
		AudioEngine? audio = svconf.Audio ? new AudioEngine() : null;
		TextSystem? text = null;
		if (svconf.Text) {
			text = new TextSystem(gpuDevice);
			assets?.RegisterResolver(ModUtils.Info.OwnerID, new FontAssetResolver(), "FontSourceAssetResolver");
			assets?.RegisterCreator(ModUtils.Info.OwnerID, new FontAssetCreator(text), "FontSourceAssetCreator");
		}
		services = new GameServices(gpuDevice, sched, eresources, assets, assetCtx, audio, text);

		// game init
		canvasResources = new CanvasSharedResources(gpuDevice, eresources);
		inputQueue = new Queue<RawInputEvent>(64);
		game.Init(services);

		// actual main loop
		double renderAccum = 0.0;
		PerfTick nextLoopDeadline = PerfTick.GetCurrent() + loopStepTicks;

		PerfTick last = PerfTick.GetCurrent();

		bool cont = true;
		while (cont) {
			while (SDL.PollEvent(&ev) == 1)
				handleEvent(&ev, game, ref cont);
			if (!cont)
				break;

			PerfTick t = PerfTick.GetCurrent();
			double dt = (double)(t - last).ToSeconds();
			last = t;

			services.AtSafeBoundary();
			InputCollector.Feed(inputQueue);

			sched.ApplyPending();
			sched.RunDueTickers();

			while (SDL.PollEvent(&ev) == 1)
				handleEvent(&ev, game, ref cont);
			if (!cont)
				break;

			switch (tmconf.RenderMode) {
			case RenderTimingMode.Capped:
				renderAccum += dt;
				if (renderAccum >= renderStep) {
					render(game);
					renderAccum -= renderStep;
					if (renderAccum > renderStep)
						renderAccum = 0.0;
				}
				break;
			case RenderTimingMode.Uncapped:
				render(game);
				break;
			}

			// make sure we don't oversleep if there's a retimed ticker
			sched.ApplyPending();

			// my best attempt to make a reasonably millisecond precise wait
			// SDL_Delay is unreliable for delays around <15ms due to OS scheduling,
			// so after a lot of experimentation and trial and error i came up with this
			if (tmconf.LoopMode == LoopTimingMode.Wait) {
				PerfTick deadline = nextLoopDeadline;
				if (sched.TryGetEarliestNextAt(out PerfTick nextTickDeadline) && nextTickDeadline < deadline)
					deadline = nextTickDeadline;

				bool restartLoop = false;
				for (;;) {
					PerfTick now = PerfTick.GetCurrent();
					if (now >= deadline)
						break;
					PerfTick remaining = deadline - now;
					if (remaining > t1_5ms) { // n > 1.5ms
						PerfTick n = remaining - t0_5ms; // 0.5ms of safety
						int ms = (int)((UInt128)n.Value * 1000 / (UInt128)PerfTick.Frequency.Value);
						if (ms > 0 && SDL.WaitEventTimeout(&ev, ms) == 1) {
							handleEvent(&ev, game, ref cont);
							while (SDL.PollEvent(&ev) == 1)
								handleEvent(&ev, game, ref cont);
							if (!cont)
								break;
							restartLoop = true; // OnHostEvent may have queued ticker changes
							break;
						}
					} else if (remaining > t0_1ms) { // 1.5ms >= n > 0.1ms
						PerfTick n = remaining - t0_1ms; // 0.1ms of safety
						PreciseWait.Wait((long)((UInt128)n.Value * 1000000000 / (UInt128)PerfTick.Frequency.Value));
					} else { // 0.1ms >= n > 0ms
						Thread.SpinWait(128);
					}
				}

				if (!cont)
					break;
				if (restartLoop)
					continue;
				nextLoopDeadline += loopStepTicks;
				PerfTick now2 = PerfTick.GetCurrent();
				if ((long)(now2.Value - nextLoopDeadline.Value) > (long)(loopStepTicks.Value * (ulong)tmconf.MaxLoopDeadlineMissByFrames))
					nextLoopDeadline = now2 + loopStepTicks;
			}
		}

		// deinit
		game.Shutdown();
		canvasResources.Dispose();
		services.Shutdown();
		text?.Dispose();
		audio?.Dispose();
		assetCtx?.Dispose();
		viewGlobals.Dispose();
		sfOutput.Dispose();
		gpuDevice.Dispose();

earlyquit:
		PreciseWait.Deinit();
		SDLOwner.ShutdownSDL();
		running = false;
	}
}
