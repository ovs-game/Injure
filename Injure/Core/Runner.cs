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
using Injure.Input;
using Injure.Layers;
using Injure.Rendering;
using Injure.Scheduling;
using Injure.Timing;

using EngineInfo = Injure.ModKit.Abstractions.EngineInfo;
using Thread = System.Threading.Thread;

namespace Injure.Core;

public static unsafe class Runner {
	private sealed class RenderController(RenderSettings initialSettings, SurfaceRenderOutput sfOutput) : IRenderController {
		private readonly SurfaceRenderOutput sfOutput = sfOutput;
		public RenderSettings Settings { get; private set; } = initialSettings;
		public bool TrySet(in RenderSettings settings, [NotNullWhen(false)] out string? err) {
			sfOutput.SetPresentModePolicy(presentModeToSfPolicy(settings.PresentMode));
			Settings = settings;
			err = null;
			return true;
		}
	}

	private sealed class TimingController(TimingSettings initialSettings) : ITimingController {
		public TimingSettings Settings { get; private set; } = initialSettings;
		public bool Changed; // check this flag at the end of every loop
		public bool TrySet(in TimingSettings settings, [NotNullWhen(false)] out string? err) {
			if (settings.RenderMode == RenderTimingMode.Capped && settings.TargetFPS <= 0.0) {
				err = "TargetFPS must be non-zero/negative if RenderMode == RenderTimingMode.Capped";
				return false;
			}
			if (settings.LoopMode == LoopTimingMode.Normal) {
				if (settings.TargetLoopHz <= 0.0) {
					err = "TargetLoopHz must be non-zero/negative if LoopMode == LoopTimingMode.Normal";
					return false;
				}
				if (settings.TargetFPS > settings.TargetLoopHz) {
					err = "TargetFPS cannot be higher than TargetLoopHz; if you want to go higher, increase TargetLoopHz";
					return false;
				}
			}
			Settings = settings;
			Changed = true;
			err = null;
			return true;
		}
	}

	public static readonly CanvasParams BaseCanvasParams = new(
		Target: CanvasTarget.Primary,
		ColorAttachmentOps: ColorAttachmentOps.Clear(Color32.Black),
		Scissor: CanvasScissor.None,
		Transform: Matrix3x2.Identity,
		OutputState: CanvasOutputStates.Alpha,
		Material: CanvasMaterials.Color
	);

	private static WebGPUDevice gpuDevice = null!;
	private static SurfaceRenderOutput sfOutput = null!;
	private static ViewGlobals viewGlobals = null!;
	private static SDLWindowController winControl = null!;

	private static GameServices services = null!;
	private static CanvasSharedResources canvasResources = null!;
	private static bool running = false;

	private static void handleEvent(in SDLEvent ev, ref bool quitRequested, InputSystem input, IGame game) {
		if ((SDLEventType)ev.Type == SDLEventType.Quit)
			quitRequested = true;
		else if (winControl.TryHandleSDLEvent(in ev, sfOutput, game))
			return;
		else if (input.TryHandleSDLEvent(in ev))
			return;
	}

	private static void render(IGame game) {
		if (!sfOutput.TryBeginFrame(out RenderFrame? frame))
			return;
		using (frame) {
			viewGlobals.Update(frame.PrimaryView.Width, frame.PrimaryView.Height);
			using (Canvas cv = new(gpuDevice, viewGlobals, frame, canvasResources, in BaseCanvasParams))
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

	private static SurfacePresentModePolicy presentModeToSfPolicy(PresentMode presentMode) => presentMode.Tag switch {
		PresentMode.Case.TearFree => SurfacePresentModePolicy.AutoMailbox,
		PresentMode.Case.Adaptive => SurfacePresentModePolicy.AutoFifoRelaxed,
		PresentMode.Case.LowLatency => SurfacePresentModePolicy.AutoImmediate,
		_ => throw new UnreachableException(),
	};

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
		if (loopStepTicks < (MonoTick)1)
			loopStepTicks = (MonoTick)1;

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
					case SDLEventType.WindowExposed:
					case SDLEventType.WindowPixelSizeChanged:
					case SDLEventType.WindowResized:
					case SDLEventType.RenderTargetsReset:
						elapsed = (double)(MonoTick.GetCurrent() - loadingStartTick) / (double)MonoTick.Frequency;
						game.Loading(new LoadingContext(LoadingPhase.Tick, elapsed, redrawRequested: true));
						break;
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
		sfOutput = new SurfaceRenderOutput(gpuDevice, SDLOwner.SurfaceHost!, presentModeToSfPolicy(rconf.Settings.PresentMode));
		viewGlobals = new ViewGlobals(gpuDevice, sfOutput.Width, sfOutput.Height);

		// controllers
		winControl = new SDLWindowController(SDLOwner.Window, winconf.Settings);
		RenderController renderControl = new(rconf.Settings, sfOutput);
		TimingController timingControl = new(tmst);

		// service/system init
		// TODO: the builtin source/resolver/creator registry should be moved out somewhere
		MonoTick budget = (MonoTick)Math.Min((ulong)loopStepTicks, (ulong)MonoTick.PeriodFromHz(tmst.TargetLoopHz));
		TickerScheduler sched = new(new TickerSchedulerOptions(MaxBatchDuration: budget));
		InputSystem input = new();
		ActionRegistry actionRegistry = new();
		LayerStack layerStack = new(sched, input);
		LayerTagRegistry layerTags = new();
		EngineResourceStore eresources = new();
		eresources.RegisterSource(new EmbeddedEngineResourceSource(
			typeof(Runner).Assembly,
			new HashSet<EngineResourceID>([
				BuiltinShaders.Primitive2D.ResourceID,
				BuiltinShaders.Textured2DColor.ResourceID,
				BuiltinShaders.Textured2DRMask.ResourceID,
				BuiltinShaders.Textured2DSDF.ResourceID,
			])
		));
		AssetStore? assets = null;
		AssetThreadContext? assetCtx = null;
		if (svconf.Assets) {
			assets = new AssetStore();
			assets.RegisterResolver(EngineInfo.OwnerID, new Texture2DJsonAssetResolver(), "Texture2DJsonAssetResolver");
			assets.RegisterResolver(EngineInfo.OwnerID, new Texture2DImageAssetResolver(), "Texture2DImageAssetResolver");
			assets.RegisterStagedCreator(EngineInfo.OwnerID, new Texture2DAssetCreator(gpuDevice), "Texture2DAssetCreator");
			assetCtx = assets.AttachCurrentThread();
		}
		AudioEngine? audio = svconf.Audio ? new AudioEngine() : null;
		TextSystem? text = null;
		if (svconf.Text) {
			text = new TextSystem(gpuDevice);
			assets?.RegisterResolver(EngineInfo.OwnerID, new FontAssetResolver(), "FontSourceAssetResolver");
			assets?.RegisterCreator(EngineInfo.OwnerID, new FontAssetCreator(text), "FontSourceAssetCreator");
		}
		services = new GameServices(
			sched,
			winControl,
			renderControl,
			timingControl,
			gpuDevice,
			actionRegistry,
			layerStack,
			layerTags,
			eresources,
			assets,
			assetCtx,
			audio,
			text
		);

		// game init
		canvasResources = new CanvasSharedResources(gpuDevice, eresources);
		game.Init(services);

		// actual main loop
		double renderAccum = 0.0;
		MonoTick nextLoopDeadline = MonoTick.GetCurrent() + loopStepTicks;

		MonoTick last = MonoTick.GetCurrent();

		bool quitRequested = false;
		while (!quitRequested) {
			while (SDL.PollEvent(&ev))
				handleEvent(in ev, ref quitRequested, input, game);
			if (quitRequested)
				break;

			MonoTick t = MonoTick.GetCurrent();
			double dt = (double)(t - last).ToSeconds();
			last = t;

			services.AtSafeBoundary();
			game.BetweenSchedulerTicks();

			sched.ApplyPending();
			sched.RunDueTickers();

			if (timingControl.Changed) {
				// XXX: race window, doesn't matter right now since most of this isn't thread safe anyways
				timingControl.Changed = false;
				tmst = timingControl.Settings;

				renderStep = tmst.TargetFPS > 0.0 ? 1.0 / tmst.TargetFPS : 0.0;
				renderAccum = 0.0;

				loopStep = tmst.TargetLoopHz > 0.0 ? 1.0 / tmst.TargetLoopHz : 0.0;
				loopStepTicks = (MonoTick)(ulong)Math.Round(loopStep * (double)MonoTick.Frequency);
				if (loopStepTicks < (MonoTick)1)
					loopStepTicks = (MonoTick)1;
				if (tmst.LoopMode == LoopTimingMode.Normal)
					nextLoopDeadline = MonoTick.GetCurrent() + loopStepTicks;
			}

			while (SDL.PollEvent(&ev))
				handleEvent(in ev, ref quitRequested, input, game);
			if (quitRequested)
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

			if (tmst.LoopMode == LoopTimingMode.Normal) {
				MonoTick deadline = nextLoopDeadline;
				if (sched.TryGetEarliestNextAt(out MonoTick nextTickDeadline) && nextTickDeadline < deadline)
					deadline = nextTickDeadline;

				bool reachedLoopDeadline = false;
				//bool restartLoop = false;
				for (;;) {
					MonoTick now = MonoTick.GetCurrent();
					if (now >= deadline) {
						reachedLoopDeadline = now >= nextLoopDeadline;
						break;
					}
					MonoTick remaining = deadline - now;
					if (remaining > t1_5ms) { // n > 1.5ms
						MonoTick n = remaining - t0_5ms; // 0.5ms of safety
						int ms = (int)((UInt128)n.Value * 1000 / (UInt128)MonoTick.Frequency.Value);
						if (ms > 0 && SDL.WaitEventTimeout(&ev, ms)) {
							handleEvent(in ev, ref quitRequested, input, game);
							while (SDL.PollEvent(&ev))
								handleEvent(in ev, ref quitRequested, input, game);
							if (quitRequested)
								break;
							// TODO: figure out what this means, i should've left a more descriptive
							// comment because i don't actually remember what this is supposed to accomplish
							//restartLoop = true; // OnHostEvent may have queued ticker changes
							break;
						}
					} else if (remaining > t0_1ms) { // 1.5ms >= n > 0.1ms
						MonoTick n = remaining - t0_1ms; // 0.1ms of safety
						PreciseWait.Wait((long)((UInt128)n.Value * 1000000000 / (UInt128)MonoTick.Frequency.Value));
					} else { // 0.1ms >= n > 0ms
						Thread.SpinWait(128);
					}
				}

				if (quitRequested)
					break;
				//if (restartLoop)
				//	continue;
				if (reachedLoopDeadline) {
					nextLoopDeadline += loopStepTicks;
					MonoTick now2 = MonoTick.GetCurrent();
					if ((long)(now2.Value - nextLoopDeadline.Value) > (long)(loopStepTicks.Value * (ulong)tmst.MaxLoopDeadlineMissByLoopDurations))
						nextLoopDeadline = now2 + loopStepTicks;
				}
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
