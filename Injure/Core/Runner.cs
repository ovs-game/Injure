// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading.Tasks;
using Hexa.NET.SDL3;

using Injure.Assets;
using Injure.Assets.Builtin;
using Injure.Audio;
using Injure.Graphics;
using Injure.Graphics.Text;
//using Injure.Input;
using Injure.Rendering;
using Injure.Scheduling;
using Injure.Timing;

using Thread = System.Threading.Thread;

namespace Injure.Core;

public static unsafe class Runner {
	private struct HostState {
		public WindowState State;
		public bool QuitRequested;
	}

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
	//[AllowNull] private static Queue<RawInputEvent> inputQueue;
	private static bool running = false;

	private static void handleEvent(SDLEvent *ev, ref HostState st, IGame game) {
		switch ((SDLEventType)ev->Type) {
		case SDLEventType.Quit:
			st.QuitRequested = true;
			break;
		/*
		TODO
		case SDLEventType.WindowPixelSizeChanged:
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
		*/
		/*
		case SDLEventType.Keydown:
		case SDLEventType.Keyup:
			if (ev->Key.Repeat == 1)
				break;
			MonoTick timestamp = MonoTick.GetCurrent();
			RawInputID id = new RawInputID(InputDeviceType.Keyboard, -1, (int)ev->Key.Keysym.Scancode);
			inputQueue.Enqueue(new RawInputEvent(id, (SDLEventType)ev->Type == SDLEventType.Keydown ? EdgeType.Press : EdgeType.Release, timestamp));
			break;
		*/
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

	private static void initSDLFrom(in WindowConfig conf) {
		uint props = SDL.CreateProperties();
		WindowSettings s = conf.Settings;
		SDL.SetStringProperty(props, SDL.SDL_PROP_WINDOW_CREATE_TITLE_STRING, s.Title);
		SDL.SetNumberProperty(props, SDL.SDL_PROP_WINDOW_CREATE_WIDTH_NUMBER, s.Width);
		SDL.SetNumberProperty(props, SDL.SDL_PROP_WINDOW_CREATE_HEIGHT_NUMBER, s.Height);
		if (!s.Visible) SDL.SetBooleanProperty(props, SDL.SDL_PROP_WINDOW_CREATE_FULLSCREEN_BOOLEAN, true);
		if (s.Resizable) SDL.SetBooleanProperty(props, SDL.SDL_PROP_WINDOW_CREATE_RESIZABLE_BOOLEAN, true);
		if (s.Borderless) SDL.SetBooleanProperty(props, SDL.SDL_PROP_WINDOW_CREATE_BORDERLESS_BOOLEAN, true);
		if (s.Fullscreen) SDL.SetBooleanProperty(props, SDL.SDL_PROP_WINDOW_CREATE_FULLSCREEN_BOOLEAN, true);
		if (conf.AllowHighDPI) SDL.SetBooleanProperty(props, SDL.SDL_PROP_WINDOW_CREATE_HIGH_PIXEL_DENSITY_BOOLEAN, true);
		switch (s.Mode.Tag) {
		case WindowMode.Case.Minimized: SDL.SetBooleanProperty(props, SDL.SDL_PROP_WINDOW_CREATE_MINIMIZED_BOOLEAN, true); break;
		case WindowMode.Case.Maximized: SDL.SetBooleanProperty(props, SDL.SDL_PROP_WINDOW_CREATE_MAXIMIZED_BOOLEAN, true); break;
		}
		int x, y;
		switch (s.Positioning.Tag) {
		case WindowPositioning.Case.Undefined: x = y = unchecked((int)SDL.SDL_WINDOWPOS_UNDEFINED_MASK); break;
		case WindowPositioning.Case.Centered: x = y = unchecked((int)SDL.SDL_WINDOWPOS_CENTERED_MASK); break;
		case WindowPositioning.Case.Explicit: x = s.X; y = s.Y; break;
		default: throw new UnreachableException(); // silence "use of unassigned local"
		}
		SDL.SetNumberProperty(props, SDL.SDL_PROP_WINDOW_CREATE_X_NUMBER, x);
		SDL.SetNumberProperty(props, SDL.SDL_PROP_WINDOW_CREATE_Y_NUMBER, y);
		SDLOwner.InitSDL(props);
	}

	public static void Run(IGame game, in GameConfig conf) {
		ServiceConfig svconf = conf.Service;
		WindowConfig winconf = conf.Window;
		RenderConfig rconf = conf.Render;
		TimingConfig tmconf = conf.Timing;
		TimingSettings tmst = tmconf.Settings;

		// validation and basic init
		if (tmst.RenderMode == RenderTimingMode.Capped) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tmst.TargetFPS);
		if (tmst.LoopMode == LoopTimingMode.Normal) {
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tmst.TargetLoopHz);
			if (tmst.TargetFPS > tmst.TargetLoopHz)
				throw new ArgumentException("TargetFPS cannot be higher than TargetLoopHz; if you want to go higher, increase TargetLoopHz");
		}

		if (running)
			throw new InvalidOperationException("an IGame instance is already running");
		running = true;

		double renderStep = tmst.TargetFPS > 0.0 ? 1.0 / tmst.TargetFPS : 0.0;
		double loopStep = tmst.TargetLoopHz > 0.0 ? 1.0 / tmst.TargetLoopHz : 0.0;
		MonoTick loopStepTicks = (MonoTick)(ulong)Math.Round(loopStep * (double)MonoTick.Frequency);

		MonoTick t1_5ms = (MonoTick)(ulong)((UInt128)MonoTick.Frequency.Value * 15 / 10000);
		MonoTick t0_5ms = (MonoTick)(ulong)((UInt128)MonoTick.Frequency.Value * 5 / 10000);
		MonoTick t0_1ms = (MonoTick)(ulong)((UInt128)MonoTick.Frequency.Value * 1 / 10000);

		// sdl/precisewait init
		initSDLFrom(in winconf);
		PreciseWait.Init();
		game.Loading(new LoadingContext(LoadingPhase.Start, redrawRequested: true));
		MonoTick loadingStartTick = MonoTick.GetCurrent();

		// webgpu bootstrap
		Task<WebGPUDevice> bootstrap = Task.Run(() => new WebGPUDevice());

		// basic loading-time event loop
		SDLEvent ev;
		double elapsed;
		bool cancelled = false;
		while (!bootstrap.IsCompleted) {
			if (!cancelled) {
				while (SDL.PollEvent(&ev)) {
					switch ((SDLEventType)ev.Type) {
					case SDLEventType.Quit:
						SDL.HideWindow(SDLOwner.Window);
						cancelled = true;
						goto bootstrapCancelled;
					/*
					TODO
					case SDLEventType.Windowevent:
						elapsed = (double)(MonoTick.GetCurrent() - loadingStartTick) / (double)MonoTick.Frequency;
						game.Loading(new LoadingContext(LoadingPhase.Tick, elapsed, redrawRequested: true));
						break;
					*/
					}
				}
				elapsed = (double)(MonoTick.GetCurrent() - loadingStartTick) / (double)MonoTick.Frequency;
				game.Loading(new LoadingContext(LoadingPhase.Tick, elapsed));
			}
bootstrapCancelled:
			SDL.Delay(10);
		}
		if (!bootstrap.IsCompletedSuccessfully)
			goto earlyquit;
		elapsed = (double)(MonoTick.GetCurrent() - loadingStartTick) / (double)MonoTick.Frequency;
		game.Loading(new LoadingContext(LoadingPhase.Finish, elapsed));

		// webgpu setup
		gpuDevice = bootstrap.Result;
		sfOutput = new SurfaceRenderOutput(gpuDevice, SDLOwner.SurfaceHost!, rconf.Settings.PresentMode.Tag switch {
			PresentMode.Case.TearFree => SurfacePresentModePolicy.AutoMailbox,
			PresentMode.Case.Adaptive => SurfacePresentModePolicy.AutoFifoRelaxed,
			PresentMode.Case.LowLatency => SurfacePresentModePolicy.AutoImmediate,
			_ => throw new UnreachableException()
		});
		viewGlobals = new ViewGlobals(gpuDevice, sfOutput.Width, sfOutput.Height);

		// service init
		// TODO: the builtin source/resolver/creator registry should be moved out somewhere
		MonoTick budget = (MonoTick)Math.Min((ulong)loopStepTicks, (ulong)MonoTick.PeriodFromHz(tmst.TargetLoopHz));
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
			assets.RegisterStagedCreator(ModUtils.Info.OwnerID, new Texture2DAssetCreator(gpuDevice), "Texture2DAssetCreator");
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
		//inputQueue = new Queue<RawInputEvent>(64);TODO
		game.Init(services);

		// actual main loop
		double renderAccum = 0.0;
		MonoTick nextLoopDeadline = MonoTick.GetCurrent() + loopStepTicks;

		MonoTick last = MonoTick.GetCurrent();

		HostState st = default;
		while (!st.QuitRequested) {
			while (SDL.PollEvent(&ev))
				handleEvent(&ev, ref st, game);
			if (st.QuitRequested)
				break;

			MonoTick t = MonoTick.GetCurrent();
			double dt = (double)(t - last).ToSeconds();
			last = t;

			services.AtSafeBoundary();
			//InputCollector.Feed(inputQueue);TODO

			sched.ApplyPending();
			sched.RunDueTickers();

			while (SDL.PollEvent(&ev))
				handleEvent(&ev, ref st, game);
			if (st.QuitRequested)
				break;

			switch (tmst.RenderMode.Tag) {
			case RenderTimingMode.Case.Capped:
				renderAccum += dt;
				if (renderAccum >= renderStep) {
					render(game);
					renderAccum -= renderStep;
					if (renderAccum > renderStep)
						renderAccum = 0.0;
				}
				break;
			case RenderTimingMode.Case.Uncapped:
				render(game);
				break;
			}

			// make sure we don't oversleep if there's a retimed ticker
			sched.ApplyPending();

			// my best attempt to make a reasonably millisecond precise wait
			// SDL_Delay is unreliable for delays around <15ms due to OS scheduling,
			// so after a lot of experimentation and trial and error i came up with this
			if (tmst.LoopMode == LoopTimingMode.Normal) {
				MonoTick deadline = nextLoopDeadline;
				if (sched.TryGetEarliestNextAt(out MonoTick nextTickDeadline) && nextTickDeadline < deadline)
					deadline = nextTickDeadline;

				bool restartLoop = false;
				for (;;) {
					MonoTick now = MonoTick.GetCurrent();
					if (now >= deadline)
						break;
					MonoTick remaining = deadline - now;
					if (remaining > t1_5ms) { // n > 1.5ms
						MonoTick n = remaining - t0_5ms; // 0.5ms of safety
						int ms = (int)((UInt128)n.Value * 1000 / (UInt128)MonoTick.Frequency.Value);
						if (ms > 0 && SDL.WaitEventTimeout(&ev, ms)) {
							handleEvent(&ev, ref st, game);
							while (SDL.PollEvent(&ev))
								handleEvent(&ev, ref st, game);
							if (st.QuitRequested)
								break;
							restartLoop = true; // OnHostEvent may have queued ticker changes
							break;
						}
					} else if (remaining > t0_1ms) { // 1.5ms >= n > 0.1ms
						MonoTick n = remaining - t0_1ms; // 0.1ms of safety
						PreciseWait.Wait((long)((UInt128)n.Value * 1000000000 / (UInt128)MonoTick.Frequency.Value));
					} else { // 0.1ms >= n > 0ms
						Thread.SpinWait(128);
					}
				}

				if (st.QuitRequested)
					break;
				if (restartLoop)
					continue;
				nextLoopDeadline += loopStepTicks;
				MonoTick now2 = MonoTick.GetCurrent();
				if ((long)(now2.Value - nextLoopDeadline.Value) > (long)(loopStepTicks.Value * (ulong)tmst.MaxLoopDeadlineMissByLoopDurations))
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
