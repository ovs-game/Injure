// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Injure.Coroutines;
using Injure.Timing;

namespace Injure.Layers;

internal sealed class LayerRuntime : ILayerPerfTracker, IDisposable {
	public LayerTimeDomain Time { get; }
	public CoroutineScheduler Coroutines { get; }
	public CoroutineScope CoroutineScope { get; }

	private readonly List<IPerfUpdateReceiver> toUpdate;

	public LayerRuntime() {
		Time = new LayerTimeDomain();
		Coroutines = new CoroutineScheduler();
		CoroutineScope = CoroutineScope.CreateRoot(Coroutines, "Layer");
		toUpdate = new List<IPerfUpdateReceiver>();
	}

	public T Track<T>(T obj) where T : class, IPerfUpdateReceiver {
		toUpdate.Add(obj);
		return obj;
	}

	public void BeforeUpdate(in LayerTickContext ctx) {
		foreach (IPerfUpdateReceiver r in toUpdate)
			r.Update(ctx.PerfTick);
	}

	public void AfterUpdate(in LayerTickContext ctx) {
		Coroutines.Tick(ctx.DeltaTime, ctx.RawDeltaTime, CoroUpdatePhase.Update);
	}

	public void Dispose() {
		CoroutineScope.Cancel();
	}
}
