// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Threading;

using Injure.Assets;
using Injure.Audio;
using Injure.Graphics.Text;
using Injure.Input;
using Injure.Layers;
using Injure.Rendering;
using Injure.Scheduling;
using static Injure.Core.GameServiceSharedUtil;

namespace Injure.Core;

internal static class GameServiceSharedUtil {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T Alive<T>(GameServiceLifetime lifetime, T obj) {
		if (lifetime.IsShutdown)
			throw new InvalidOperationException("game services are no longer available after shutdown");
		return obj;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T AliveAndNonnull<T>(GameServiceLifetime lifetime, T? obj, string msg) where T : class {
		if (lifetime.IsShutdown)
			throw new InvalidOperationException("game services are no longer available after shutdown");
		if (obj is null)
			throw new InvalidOperationException(msg);
		return obj;
	}
}

internal sealed class GameServiceLifetime {
	private int shutdown = 0;
	public bool IsShutdown {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Volatile.Read(ref shutdown) != 0;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Shutdown() => Volatile.Write(ref shutdown, 1);
}

public sealed class HostServices {
	private readonly GameServiceLifetime lifetime;
	public IWindowController Window { get => Alive(lifetime, field); }
	public IRenderController Render { get => Alive(lifetime, field); }
	public ITimingController Timing { get => Alive(lifetime, field); }

	internal HostServices(GameServiceLifetime lifetime, IWindowController window, IRenderController render, ITimingController timing) {
		this.lifetime = lifetime;
		Window = window;
		Render = render;
		Timing = timing;
	}
}

public sealed class GraphicsServices {
	private readonly GameServiceLifetime lifetime;
	public WebGPUDevice Device { get => Alive(lifetime, field); }

	internal GraphicsServices(GameServiceLifetime lifetime, WebGPUDevice device) {
		this.lifetime = lifetime;
		Device = device;
	}
}

public sealed class InputServices {
	private readonly GameServiceLifetime lifetime;
	public ActionRegistry Actions { get => Alive(lifetime, field); }

	internal InputServices(GameServiceLifetime lifetime, ActionRegistry actions) {
		this.lifetime = lifetime;
		Actions = actions;
	}
}

public sealed class LayerServices {
	private readonly GameServiceLifetime lifetime;
	public LayerStack Stack { get => Alive(lifetime, field); }
	public LayerTagRegistry Tags { get => Alive(lifetime, field); }

	internal LayerServices(GameServiceLifetime lifetime, LayerStack stack, LayerTagRegistry tags) {
		this.lifetime = lifetime;
		Stack = stack;
		Tags = tags;
	}
}

public sealed class AdvancedServices {
	private readonly GameServiceLifetime lifetime;
	public EngineResourceStore EngineResources { get => Alive(lifetime, field); }
	public AssetThreadContext AssetMainThreadContext { get => AliveAndNonnull(lifetime, field, "assets subsystem is not enabled"); }

	internal AdvancedServices(GameServiceLifetime lifetime, EngineResourceStore engineResources, AssetThreadContext? assetMainThreadCtx) {
		this.lifetime = lifetime;
		EngineResources = engineResources;
		AssetMainThreadContext = assetMainThreadCtx;
	}
}

public sealed class GameServices {
	private readonly GameServiceLifetime lifetime;
	private readonly AssetStore? assets;
	private readonly AssetThreadContext? assetMainThreadCtx;
	private readonly AudioEngine? audio;
	private readonly TextSystem? text;

	// required:
	public ITickerRegistry Tickers { get => Alive(lifetime, field); }
	public HostServices Host { get => Alive(lifetime, field); }
	public GraphicsServices Graphics { get => Alive(lifetime, field); }
	public InputServices Input { get => Alive(lifetime, field); }
	public LayerServices Layers { get => Alive(lifetime, field); }
	public AdvancedServices Advanced { get => Alive(lifetime, field); }

	// optional:
	public AssetStore Assets { get => AliveAndNonnull(lifetime, assets, "assets subsystem is not enabled"); }
	public AudioEngine Audio { get => AliveAndNonnull(lifetime, audio, "audio subsystem is not enabled"); }
	public TextSystem Text { get => AliveAndNonnull(lifetime, text, "text subsystem is not enabled"); }
	public bool HasAssets => Alive(lifetime, assets is not null);
	public bool HasAudio => Alive(lifetime, audio is not null);
	public bool HasText => Alive(lifetime, text is not null);

	internal GameServices(
		ITickerRegistry tickers,
		IWindowController windowController,
		IRenderController renderController,
		ITimingController timingController,
		WebGPUDevice gpuDevice,
		ActionRegistry actionRegistry,
		LayerStack layerStack,
		LayerTagRegistry layerTags,
		EngineResourceStore engineResources,
		AssetStore? assets,
		AssetThreadContext? assetMainThreadCtx,
		AudioEngine? audio,
		TextSystem? text
	) {
		if ((assets is null) ^ (assetMainThreadCtx is null))
			throw new InternalStateException("was expecting either none of or both the asset store and asset thread context to be null");
		lifetime = new GameServiceLifetime();
		Tickers = tickers;
		Host = new HostServices(lifetime, windowController, renderController, timingController);
		Graphics = new GraphicsServices(lifetime, gpuDevice);
		Input = new InputServices(lifetime, actionRegistry);
		Layers = new LayerServices(lifetime, layerStack, layerTags);
		Advanced = new AdvancedServices(lifetime, engineResources, assetMainThreadCtx);
		this.assets = assets;
		this.assetMainThreadCtx = assetMainThreadCtx;
		this.audio = audio;
		this.text = text;
	}

	internal void AtSafeBoundary() {
		assetMainThreadCtx?.AtSafeBoundary();
		assets?.ApplyQueuedReloads();
	}

	internal void Shutdown() => lifetime.Shutdown();
}
