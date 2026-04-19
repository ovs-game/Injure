// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Injure.Coroutines;
using Injure.Timing;

namespace Injure.Layers;

internal sealed class LayerRuntime : ILayerTickTracker, IDisposable {
	public LayerTimeDomain Time { get; }
	public CoroutineScheduler Coroutines { get; }
	public CoroutineScope CoroutineScope { get; }

	private readonly List<ITickTimestampReceiver> toUpdate;

	public LayerRuntime() {
		Time = new LayerTimeDomain();
		Coroutines = new CoroutineScheduler();
		CoroutineScope = CoroutineScope.CreateRoot(Coroutines, "Layer");
		toUpdate = new List<ITickTimestampReceiver>();
	}

	public T Track<T>(T obj) where T : class, ITickTimestampReceiver {
		toUpdate.Add(obj);
		return obj;
	}

	public void BeforeUpdate(in LayerTickContext ctx) {
		foreach (ITickTimestampReceiver r in toUpdate)
			r.Update(ctx.Tick);
	}

	public void AfterUpdate(in LayerTickContext ctx) {
		Coroutines.Tick(ctx.DeltaTime, ctx.RawDeltaTime, CoroUpdatePhase.Update);
	}

	public void Dispose() {
		CoroutineScope.Cancel();
	}
}
