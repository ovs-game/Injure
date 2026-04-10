// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using Injure.Assets;
using Injure.Audio;
using Injure.Graphics.Text;
using Injure.Rendering;

namespace Injure.Core;

public sealed class GameServices {
	private readonly WebGPUDevice gpuDevice;
	private readonly ITickerRegistry tickers;
	private readonly EngineResourceStore engineResources;
	private readonly AssetStore? assets;
	private readonly AssetThreadContext? assetCtx;
	private readonly AudioEngine? audio;
	private readonly TextSystem? text;
	private bool shutdown = false;

	// required:
	public WebGPUDevice GPUDevice { get => alive(gpuDevice); }
	public ITickerRegistry Tickers { get => alive(tickers); }
	public EngineResourceStore EngineResources { get => alive(engineResources); }

	// optional:
	public AssetStore Assets { get => aliveAndNonnull(assets, "assets subsystem is not enabled"); }
	public AudioEngine Audio { get => aliveAndNonnull(audio, "audio subsystem is not enabled"); }
	public TextSystem Text { get => aliveAndNonnull(text, "text subsystem is not enabled"); }
	public bool HaveAssets => assets is not null;
	public bool HaveAudio => audio is not null;
	public bool HaveText => text is not null;

	internal GameServices(WebGPUDevice gpuDevice, ITickerRegistry tickers, EngineResourceStore engineResources,
			AssetStore? assets, AssetThreadContext? assetCtx, AudioEngine? audio, TextSystem? text) {
		if ((assets is null) ^ (assetCtx is null))
			throw new InternalStateException("was expecting either none of or both the asset store and asset thread context to be null");
		this.gpuDevice = gpuDevice;
		this.tickers = tickers;
		this.engineResources = engineResources;
		this.assets = assets;
		this.assetCtx = assetCtx;
		this.audio = audio;
		this.text = text;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private T alive<T>(T obj) {
		if (shutdown)
			throw new InvalidOperationException("game services are no longer available after shutdown");
		return obj;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private T aliveAndNonnull<T>(T? obj, string msg) where T : class {
		if (shutdown)
			throw new InvalidOperationException("game services are no longer available after shutdown");
		if (obj is null)
			throw new InvalidOperationException(msg);
		return obj;
	}

	internal void AtSafeBoundary() {
		assetCtx?.AtSafeBoundary();
		assets?.ApplyQueuedReloads();
	}

	internal void Shutdown() {
		if (shutdown)
			return;
		shutdown = true;
	}
}
