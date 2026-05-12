// SPDX-License-Identifier: MIT

using System;

using Injure.Analyzers.Attributes;
using Injure.Coroutines;
using Injure.Graphics;
using Injure.Input;

namespace Injure.Layers;

[ClosedFlags]
public readonly partial struct LayerFeatures {
	[Flags]
	public enum Bits {
		None   = 0,
		Render = 1 << 0,
		Input  = 1 << 1,
	}
}

[ClosedFlags]
public readonly partial struct LayerBlockMask {
	[Flags]
	public enum Bits {
		None   = 0,
		Update = 1 << 0,
		Render = 1 << 1,
		Input  = 1 << 2,
	}
}

public readonly record struct LayerBlockRule(LayerBlockMask Blocked, LayerTagSet MatchTags);

public abstract class Layer {
	internal LayerStack? Owner { get; set; }
	internal LayerRuntime? Runtime { get; private set; }

	private const string eMsg = "activation-bound layer services (time domain, coroutines, tracking) are not available yet (most likely, you have to move this code from the constructor to OnEnter)";
	protected LayerTimeDomain Time => Runtime?.Time ?? throw new InvalidOperationException(eMsg);
	protected CoroutineScheduler Coroutines => Runtime?.Coroutines ?? throw new InvalidOperationException(eMsg);
	protected CoroutineScope CoroutineScope => Runtime?.CoroutineScope ?? throw new InvalidOperationException(eMsg);
	protected ILayerTickTracker TickTracker => Runtime ?? throw new InvalidOperationException(eMsg);

	internal void AttachRuntime(LayerRuntime runtime) {
		Runtime = runtime ?? throw new InternalStateException("AttachRuntime got passed null");
	}

	internal void DetachRuntime() {
		if (Runtime is null)
			throw new InternalStateException("no layer runtime is attached");
		Runtime.Dispose();
		Runtime = null;
	}

	public virtual LayerFeatures Features => LayerFeatures.Render;
	public virtual LayerTagSet Tags => LayerTagSet.Empty;
	public virtual ReadOnlySpan<LayerBlockRule> BlockRules => ReadOnlySpan<LayerBlockRule>.Empty;
	public virtual ActionProfile? ActionProfile => null;

	public abstract void OnEnter();
	public abstract void Update(in LayerTickContext ctx);
	public abstract void Render(Canvas cv);
	public abstract void OnLeave();
}
