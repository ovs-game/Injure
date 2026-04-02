// SPDX-License-Identifier: MIT

namespace Injure.Coroutines;

public readonly struct CoroutineContext {
	public CoroutineScheduler Scheduler { get; }
	public CoroutineHandle Handle { get; }
	public CoroutineScope Scope { get; }
	public double DeltaTime { get; }
	public double RawDeltaTime { get; }
	public CoroUpdatePhase Phase { get; }
	public CoroutineTick Tick { get; }

	internal CoroutineContext(CoroutineScheduler sched, CoroutineHandle handle, CoroutineScope scope,
		double dt, double rawDt, CoroUpdatePhase phase, CoroutineTick tick) {
		Scheduler = sched;
		Handle = handle;
		Scope = scope;
		DeltaTime = dt;
		RawDeltaTime = rawDt;
		Phase = phase;
		Tick = tick;
	}
}
