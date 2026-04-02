// SPDX-License-Identifier: MIT

using System;

using Injure.Assets;
using Injure.Audio;
using Injure.Graphics.Text;

namespace Injure.Core;

public sealed class GameServices {
	private readonly ITickerRegistry tickers;
	private readonly EngineResourceStore engineResources;
	private readonly AssetStore? assets;
	private readonly AssetThreadContext? assetCtx;
	private readonly AudioEngine? audio;
	private readonly TextSystem? text;
	private bool shutdown = false;

	// required:
	public ITickerRegistry Tickers { get => alive(tickers); }
	public EngineResourceStore EngineResources { get => alive(engineResources); }

	// optional:
	public AssetStore Assets { get => aliveAndNonnull(assets, "assets subsystem is not enabled"); }
	public AudioEngine Audio { get => aliveAndNonnull(audio, "audio subsystem is not enabled"); }
	public TextSystem Text { get => aliveAndNonnull(text, "text subsystem is not enabled"); }
	public bool HaveAssets => assets is not null;
	public bool HaveAudio => audio is not null;
	public bool HaveText => text is not null;

	internal GameServices(ITickerRegistry tickers, EngineResourceStore engineResources, AssetStore? assets, AssetThreadContext? assetCtx, AudioEngine? audio, TextSystem? text) {
		if ((assets is null) ^ (assetCtx is null))
			throw new InternalStateException("was expecting either none of or both the asset store and asset thread context to be null");
		this.tickers = tickers;
		this.engineResources = engineResources;
		this.assets = assets;
		this.assetCtx = assetCtx;
		this.audio = audio;
		this.text = text;
	}

	private T alive<T>(T obj) {
		if (shutdown)
			throw new InvalidOperationException("game services are no longer available after shutdown");
		return obj;
	}

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
		text?.Dispose();
		audio?.Dispose();
		assetCtx?.Dispose();
	}
}
