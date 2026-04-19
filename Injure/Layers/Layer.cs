// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using Injure.Coroutines;
using Injure.Graphics;

namespace Injure.Layers;

[Flags]
public enum LayerPassMask {
	None   = 0,
	Update = 1 << 0,
	Render = 1 << 1,
	Input  = 1 << 2
}

public readonly record struct LayerBlockRule(
	LayerPassMask Passes,
	LayerTagSet MatchTags
);

public abstract class Layer {
	internal LayerStack? Owner { get; set; }
	internal LayerRuntime? Runtime { get; private set; }

	private const string eMsg = "activation-bound layer services (time domain, coroutines, tracking) are not available yet (most likely, you have to move this code from the constructor to OnEnter)";
	protected LayerTimeDomain Time => Runtime?.Time ?? throw new InvalidOperationException(eMsg);
	protected CoroutineScheduler Coroutines => Runtime?.Coroutines ?? throw new InvalidOperationException(eMsg);
	protected CoroutineScope CoroutineScope => Runtime?.CoroutineScope ?? throw new InvalidOperationException(eMsg);
	protected ILayerTickTracker TickTracker => Runtime ?? throw new InvalidOperationException(eMsg);

	[MemberNotNull(nameof(Runtime))]
	internal void OnEnterCore() {
		Runtime = new LayerRuntime();
		OnEnter();
	}
	internal void UpdateCore(in LayerTickContext ctx) {
		if (Runtime is null)
			throw new InternalStateException("expected layer runtime instance to be nonnull by this point");
		Runtime.BeforeUpdate(in ctx);
		Update(in ctx);
		Runtime.AfterUpdate(in ctx);
	}
	internal void RenderCore(Canvas cv) {
		if (Runtime is null)
			throw new InternalStateException("expected layer runtime instance to be nonnull by this point");
		Render(cv);
	}
	internal void OnLeaveCore() {
		if (Runtime is null)
			throw new InternalStateException("expected layer runtime instance to be nonnull by this point");
		try {
			OnLeave();
		} finally {
			Runtime.Dispose();
			Runtime = null;
		}
	}

	public virtual LayerPassMask ParticipatingPasses => LayerPassMask.Update | LayerPassMask.Render;
	public virtual ReadOnlySpan<LayerTag> Tags => ReadOnlySpan<LayerTag>.Empty;
	public virtual ReadOnlySpan<LayerBlockRule> BlockRules => ReadOnlySpan<LayerBlockRule>.Empty;

	public abstract void OnEnter();
	public abstract void Update(in LayerTickContext ctx);
	public abstract void Render(Canvas cv);
	public abstract void OnLeave();
}
