// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Threading;

namespace Injure.Coroutines;

public static class CoroWait {
	public static ICoroutineWait Ticks(CoroutineTick ticks) =>
		ticks >= CoroutineTick.Zero ? new CoroWaitForTicks(ticks) : throw new ArgumentOutOfRangeException(nameof(ticks));
	public static ICoroutineWait Ticks(int ticks) => Ticks((CoroutineTick)ticks); // quality of life overload for int literals
	public static ICoroutineWait Seconds(double seconds) =>
		seconds >= 0 ? new CoroWaitForSeconds(seconds) : throw new ArgumentOutOfRangeException(nameof(seconds));
	public static ICoroutineWait ForHandle(CoroutineHandle handle, bool propagateFault = true, bool throwOnChildCancelled = false) =>
		new CoroWaitForHandle(handle, propagateFault, throwOnChildCancelled);
	public static ICoroutineWait Until(Func<bool> predicate, string? debugDesc = null) =>
		new CoroWaitUntilPredicate(predicate ?? throw new ArgumentNullException(nameof(predicate)), invert: false, debugDesc);
	public static ICoroutineWait While(Func<bool> predicate, string? debugDesc = null) =>
		new CoroWaitUntilPredicate(predicate ?? throw new ArgumentNullException(nameof(predicate)), invert: true, debugDesc);
}

public sealed class CoroSignal {
	private int val = 0;
	public void Signal() => Interlocked.Exchange(ref val, 1);
	public void Reset() => Interlocked.Exchange(ref val, 0);
	public ICoroutineWait Wait(string? debugDesc = null) => new CoroWaitForSignal(this, debugDesc);
	internal bool TryConsumeSignal() => Interlocked.Exchange(ref val, 0) != 0;
}

internal sealed class CoroWaitForTicks(CoroutineTick ticks) : ICoroutineWait {
	private readonly CoroutineTick total = ticks;
	private CoroutineTick remaining = ticks;
	public bool KeepWaiting(in CoroutineContext ctx) => (remaining > CoroutineTick.Zero) && (--remaining > CoroutineTick.Zero);
	public void OnCancel(CoroCancellationReason reason) {}
	public string GetDebugWaitDescription() => $"for {remaining} more ticks (started at {total})";
}

internal sealed class CoroWaitUntilTick(CoroutineTick targetTick) : ICoroutineWait {
	private readonly CoroutineTick target = targetTick;
	public bool KeepWaiting(in CoroutineContext ctx) => ctx.Tick < target;
	public void OnCancel(CoroCancellationReason reason) {}
	public string GetDebugWaitDescription() => $"until tick {target}";
}

internal sealed class CoroWaitForSeconds(double seconds) : ICoroutineWait {
	private readonly double total = seconds;
	private double remaining = seconds;
	public bool KeepWaiting(in CoroutineContext ctx) => (remaining -= ctx.DeltaTime) > 0f;
	public void OnCancel(CoroCancellationReason reason) {}
	public string GetDebugWaitDescription() => $"for {Math.Max(remaining, 0f):0.###} more seconds (started at {total:0.###})";
}

internal sealed class CoroWaitForHandle(CoroutineHandle handle, bool propagateFault, bool throwOnChildCancelled) : ICoroutineWait {
	private readonly CoroutineHandle handle = handle;
	private readonly bool propagateFault = propagateFault;
	private readonly bool throwOnChildCancelled = throwOnChildCancelled;
	private bool attached = false;

	public CoroutineHandle TargetHandle => handle;

	public bool EnsureAttached(CoroutineScheduler scheduler) {
		if (attached)
			return true;
		if (!scheduler.TryRetainHandle(handle))
			return false;
		attached = true;
		return true;
	}

	public void Detach(CoroutineScheduler scheduler) {
		if (!attached)
			return;
		scheduler.ReleaseRetainedHandle(handle);
		attached = false;
	}

	public bool KeepWaiting(in CoroutineContext ctx) {
		if (handle == ctx.Handle)
			throw new InvalidOperationException($"coroutine {ctx.Handle} tried to wait on its own handle");
		if (!ctx.Scheduler.TryGetInfo(handle, out CoroutineInfo info))
			throw new InvalidOperationException($"failed to get info for coroutine handle {handle}");
		switch (info.Status) {
			case CoroutineStatus.Running:
			case CoroutineStatus.Paused:
				return true;
			case CoroutineStatus.Completed:
				return false;
			case CoroutineStatus.Cancelled:
				if (throwOnChildCancelled)
					throw new CoroutineCancelledException(handle, info.CancellationReason ?? CoroCancellationReason.ManualStop);
				return false;
			case CoroutineStatus.Faulted:
				// XXX we need a nicer api than just "null-suppress Fault if the status is Faulted"
				if (propagateFault)
					throw new CoroutineChildFaultException(handle, info.Fault!);
				return false;
			default:
				throw new UnreachableException();
		}
	}
	public void OnCancel(CoroCancellationReason reason) {}
	public string GetDebugWaitDescription() => $"for handle {handle}";
}

internal sealed class CoroWaitUntilPredicate(Func<bool> predicate, bool invert, string? debugDesc = null) : ICoroutineWait {
	private readonly Func<bool> predicate = predicate;
	private readonly bool invert = invert;
	private readonly string? debugDesc = debugDesc;
	public bool KeepWaiting(in CoroutineContext ctx) {
		bool v = predicate();
		return invert ? v : !v;
	}
	public void OnCancel(CoroCancellationReason reason) {}
	public string GetDebugWaitDescription() {
		if (!string.IsNullOrEmpty(debugDesc))
			return debugDesc;
		return invert ? "while predicate returns true" : "until predicate returns true";
	}
}

internal sealed class CoroWaitForSignal(CoroSignal signal, string? debugDesc = null) : ICoroutineWait {
	private readonly CoroSignal signal = signal;
	private readonly string? debugDesc = debugDesc;
	public bool KeepWaiting(in CoroutineContext ctx) => !signal.TryConsumeSignal();
	public void OnCancel(CoroCancellationReason reason) {}
	public string GetDebugWaitDescription() => debugDesc ?? "for a signal";
}
