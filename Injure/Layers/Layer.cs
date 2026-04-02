// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

using Injure.Core;
using Injure.Coroutines;
using Injure.Graphics;

namespace Injure.Layers;

// TODO: something a bit more Comprehensive than this, tag system, blocking tick/render for
// lower layers based on tags, etc etc
public abstract class Layer {
	internal LayerStack? Owner { get; set; }
	internal TickerHandle Ticker { get; set; }

	[AllowNull] protected CoroutineScheduler Coroutines { get; private set; }
	[AllowNull] protected CoroutineScope CoroutineScope { get; private set; }

	public double TimeScale { get; set; } = 1.0;
	public bool TimePaused { get; set; } = false;

	[MemberNotNull(nameof(Coroutines), nameof(CoroutineScope))]
	internal void OnEnterCore() {
		Coroutines = new CoroutineScheduler();
		CoroutineScope = CoroutineScope.CreateRoot(Coroutines, "TODO: figure out what to set this to");
		OnEnter();
	}
	internal void TickCore(in LayerTickContext ctx) {
		Tick(in ctx);
		Coroutines.Tick(ctx.DeltaTime, ctx.RawDeltaTime, CoroUpdatePhase.Update);
	}
	internal void RenderCore(Canvas cv) => Render(cv);
	internal void OnLeaveCore() {
		try {
			OnLeave();
		} finally {
			CoroutineScope.Cancel();
		}
	}

	protected internal virtual double TransformDeltaTime(double rawDt, in TickCallbackInfo tickInfo) =>
		TimePaused ? 0.0 : rawDt * TimeScale;

	// TODO: use something that isn't a virtual property
	public virtual bool WantInputEvents => false;

	public abstract void OnEnter();
	public abstract void Tick(in LayerTickContext ctx);
	public abstract void Render(Canvas cv);
	public abstract void OnLeave();
}
