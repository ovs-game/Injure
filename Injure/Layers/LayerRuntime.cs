// SPDX-License-Identifier: MIT

using System;

using Injure.Coroutines;

namespace Injure.Layers;

public sealed class LayerRuntime : IDisposable {
	public LayerTimeDomain Time { get; } = new LayerTimeDomain();
	public CoroutineScheduler Coroutines { get; } = new CoroutineScheduler();
	public CoroutineScope CoroutineScope { get; }

	public LayerRuntime() {
		CoroutineScope = CoroutineScope.CreateRoot(Coroutines, "Layer");
	}

	public void AfterUpdate(in LayerTickContext ctx) {
		Coroutines.Tick(ctx.DeltaTime, ctx.RawDeltaTime, CoroUpdatePhase.Update);
	}

	public void Dispose() {
		CoroutineScope.Cancel();
	}
}
