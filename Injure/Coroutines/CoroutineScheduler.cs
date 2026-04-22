// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Injure.Coroutines;

/// <summary>
/// Schedules and runs coroutines bound to <see cref="CoroutineScope"/>s.
/// </summary>
/// <remarks>
/// Coroutines are advanced by calling <see cref="Tick(double, double, CoroUpdatePhase)"/>.
/// When in the middle of a coroutine step, control operations (cancel/pause/resume) are
/// deferred to be applied at safe points instead of interrupting execution on the spot.
/// </remarks>
public sealed class CoroutineScheduler {
	// ==========================================================================
	// internal types
	private sealed class CoroutineStackFrame(IEnumerator enumerator,
			string debugName, string sourceFile = "", int sourceLine = 0, string sourceMember = "") : IDisposable {
		public IEnumerator Enumerator = enumerator;
		public string DebugName = debugName;
		public string SourceFile = sourceFile;
		public int SourceLine = sourceLine;
		public string SourceMember = sourceMember;

		public void Dispose() => (Enumerator as IDisposable)?.Dispose();
	}

	private enum PendingControlAction {
		None,
		Pause,
		Resume
	}

	private sealed class CoroutineInstance {
		public required CoroutineHandle Handle { get; init; }
		public required CoroutineScope? Scope { get => terminated ? null : field; init; }
		public required CoroutineOptions Options { get; init; }
		public ICoroutineWait? Wait { get; private set; }
		public CoroutineStatus Status;
		public Exception? Fault;
		public CoroCancellationReason? CancellationReason;
		public CoroCancellationReason? PendingCancellationReason;
		public PendingControlAction PendingControl;
		public CoroUpdatePhase LastPhase;
		public CoroutineTick StartTick;
		public CoroutineTick TerminalTick;
		public CoroutineTrace? PreservedTerminalTrace;
		private bool terminated = false;

		// not a Stack<CoroutineStackFrame> because there was kind of no reason
		// to and this makes dumping the entire stack / accessing a frame N deep / etc
		// easier
		private readonly List<CoroutineStackFrame> stack = new List<CoroutineStackFrame>();
		public int StackDepth => stack.Count;

		// not named Stack because it's a list not a stack
		public IReadOnlyList<CoroutineStackFrame> StackFrames => stack;

		public void StackPush(IEnumerator enumerator,
				string debugName, string sourceFile = "", int sourceLine = 0, string sourceMember = "") =>
			stack.Add(new CoroutineStackFrame(enumerator, debugName, sourceFile, sourceLine, sourceMember));

		public CoroutineStackFrame StackPeek() =>
			(stack.Count > 0) ? stack[^1] : throw new InternalStateException("coroutine instance stack is empty");

		public void StackPopAndDispose() {
			if (stack.Count == 0)
				throw new InternalStateException("coroutine instance stack is empty");
			int idx = stack.Count - 1;
			CoroutineStackFrame frame = stack[idx];
			try { frame.Dispose(); } catch {}
			stack.RemoveAt(idx);
		}

		private void unwind() {
			for (int i = stack.Count - 1; i >= 0; i--)
				try { stack[i].Dispose(); } catch {}
			stack.Clear();
		}

		private void clearScope() {
			Scope?.Unregister(Handle);
			terminated = true;
		}

		public bool TrySetWait(CoroutineScheduler sched, ICoroutineWait wait, [NotNullWhen(false)] out Exception? ex) {
			// TODO: this is kind of bolted on as opposed to properly delegated somewhere
			if (wait is CoroWaitForHandle hwait) {
				if (hwait.TargetHandle == Handle) {
					ex = new InvalidOperationException($"coroutine {Handle} tried to wait on its own handle");
					return false;
				}
				if (!hwait.EnsureAttached(sched)) {
					ex = new InvalidOperationException($"failed to attach coroutine handle {hwait.TargetHandle} to scheduler (likely invalid handle)");
					return false;
				}
			}
			ex = null;
			Wait = wait;
			return true;
		}

		public void ClearWait(CoroutineScheduler sched, CoroCancellationReason? reason = null) {
			if (Wait is null)
				return;
			if (reason is CoroCancellationReason r) {
				// TODO: decide what to do in catch
				try { Wait.OnCancel(r); } catch {}
			}
			// TODO: this is kind of bolted on as opposed to properly delegated somewhere
			if (Wait is CoroWaitForHandle hwait)
				hwait.Detach(sched);
			Wait = null;
		}

		public void TransitionToCompleted(CoroutineTick currTick) {
			if (StackDepth != 0)
				throw new InternalStateException("cannot transition coroutine to Completed with a non-empty stack");
			if (Wait is not null)
				throw new InternalStateException("cannot transition coroutine to Completed with an attached wait");
			clearScope();
			Status = CoroutineStatus.Completed;
			TerminalTick = currTick;
		}

		public void TransitionToCancelled(CoroutineScheduler sched, CoroCancellationReason reason, CoroutineTick currTick) {
			ClearWait(sched, reason);
			clearScope();
			unwind();
			Status = CoroutineStatus.Cancelled;
			CancellationReason = reason;
			TerminalTick = currTick;
		}

		public void TransitionToFaulted(CoroutineScheduler sched, Exception ex, CoroutineTick currTick) {
			ClearWait(sched, CoroCancellationReason.FaultPropagation);
			clearScope();
			unwind();
			Status = CoroutineStatus.Faulted;
			Fault = ex;
			TerminalTick = currTick;
		}
	}

	private sealed class CoroutineSlot {
		public int Generation;
		public CoroutineInstance? Instance;
		public CoroutineInfo? TerminalInfo;
		public CoroutineTrace? TerminalTrace;
		public int RetainCount;
	}

	// ==========================================================================
	// internal objects / properties
	private readonly List<CoroutineSlot> slots = new List<CoroutineSlot>();
	private readonly Queue<int> freeSlots = new Queue<int>();
	private readonly List<int> activeSlots = new List<int>();
	private readonly List<int> pendingActivation = new List<int>();
	private readonly HashSet<int> pendingReap = new HashSet<int>();
	private readonly List<CoroutineUnhandledFaultInfo> pendingUnhandledFaults = new List<CoroutineUnhandledFaultInfo>();
	private bool ticking;
	private CoroutineTick tick;

	/// <summary>
	/// Controls how unhandled coroutine faults are processed after the scheduler
	/// is finished with the current tick.
	/// </summary>
	/// <remarks>
	/// "Unhandled" means coroutine faults that are not otherwise observed or
	/// propagated through structured child execution or explicit handle waits.
	/// Processing is done after faulted coroutines have already been transitioned
	/// to terminal state, unwound, recorded, and queued for reaping.
	/// </remarks>
	public CoroUnhandledFaultMode UnhandledFaultMode { get; set; } = CoroUnhandledFaultMode.LogAndThrowAfterTick;

	/// <summary>
	/// Receives formatted diagnostic messages emitted by the scheduler.
	/// </summary>
	/// <remarks>
	/// Primarily used to report unhandled coroutine faults. Only used when the
	/// selected <see cref="UnhandledFaultMode"/> includes logging.
	/// </remarks>
	public Action<string> DiagnosticLogSink { get; set; } = static msg => Console.Error.WriteLine(msg);

	/// <summary>
	/// Invoked for each unhandled coroutine fault after the faulted coroutine has
	/// already been transitioned to terminal state.
	/// </summary>
	/// <remarks>
	/// This callback runs during end-of-tick fault processing, after scheduler
	/// bookkeeping for the faulting coroutine. Throwing from this callback will
	/// abort fault processing for the tick.
	/// </remarks>
	public Action<CoroutineUnhandledFaultInfo>? UnhandledFault { get; set; } = null;

	// ==========================================================================
	// public api
	private CoroutineHandle start(IEnumerator routine, CoroutineScope scope, bool defer, CoroutineOptions? options,
			string callerFile, int callerLine, string callerMember) {
		ArgumentNullException.ThrowIfNull(routine);
		ArgumentNullException.ThrowIfNull(scope);
		if (!ReferenceEquals(scope.Scheduler, this))
			throw new ArgumentException("scope belongs to a different scheduler", nameof(scope));
		options ??= new CoroutineOptions();
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxStepsPerTick);
		if (scope.Cancelled)
			throw new InvalidOperationException("scope is already cancelled");

		CoroutineHandle handle = makeHandle();
		CoroutineInstance inst = new CoroutineInstance {
			Handle = handle,
			Scope = scope,
			Options = options,
			Status = CoroutineStatus.Running,
			StartTick = tick
		};
		string debugName = options.Name ?? enumDebugName(routine) ?? CoroNameCleanup.Clean(routine.GetType().Name);
		inst.StackPush(routine, debugName,
			enumSourceFile(routine) ?? callerFile,
			enumSourceLine(routine) ?? callerLine,
			enumSourceMember(routine) ?? callerMember);
		slots[handle.Slot].TerminalInfo = null;
		slots[handle.Slot].TerminalTrace = null;
		slots[handle.Slot].RetainCount = 0;
		if (!scope.TryRegister(handle)) {
			slots[handle.Slot].Instance = null;
			freeSlots.Enqueue(handle.Slot);
			throw new InvalidOperationException("failed to register coroutine handle (most likely, this same handle is already registered in this scope)");
		}
		slots[handle.Slot].Instance = inst;
		(defer ? pendingActivation : activeSlots).Add(handle.Slot);
		return handle;
	}

	/// <summary>
	/// Creates a coroutine bound to the specified scope and schedules it to
	/// be started on the next tick.
	/// </summary>
	/// <param name="routine">The root iterator to run.</param>
	/// <param name="scope">The lifetime scope to bind to. Must belong to this scheduler.</param>
	/// <param name="options">Optional coroutine options. When <see langword="null"/>, defaults are used.</param>
	/// <param name="callerFile">Caller file path, used for debug info.</param>
	/// <param name="callerLine">Caller line number, used for debug info.</param>
	/// <param name="callerMember">Caller member name, used for debug info.</param>
	/// <returns>
	/// A handle for the created coroutine.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="routine"/> or <paramref name="scope"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="scope"/> belongs to a different scheduler.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="options"/> specifies a negative or zero
	/// <see cref="CoroutineOptions.MaxStepsPerTick"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="scope"/> is a cancelled scope or registering the coroutine
	/// handle in the scope failed.
	/// </exception>
	public CoroutineHandle Start(IEnumerator routine, CoroutineScope scope, CoroutineOptions? options = null,
			[CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0, [CallerMemberName] string callerMember = "") =>
		start(routine, scope, defer: true, options, callerFile, callerLine, callerMember);

	/// <summary>
	/// Creates a coroutine bound to the specified scope and immediately starts it.
	/// </summary>
	/// <param name="routine">The root iterator to run.</param>
	/// <param name="scope">The lifetime scope to bind to. Must belong to this scheduler.</param>
	/// <param name="options">Optional coroutine options. When <see langword="null"/>, defaults are used.</param>
	/// <param name="callerFile">Caller file path, used for debug info.</param>
	/// <param name="callerLine">Caller line number, used for debug info.</param>
	/// <param name="callerMember">Caller member name, used for debug info.</param>
	/// <returns>
	/// A handle for the started coroutine.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="routine"/> or <paramref name="scope"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="scope"/> belongs to a different scheduler.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="options"/> specifies a negative or zero
	/// <see cref="CoroutineOptions.MaxStepsPerTick"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the scheduler is currently executing a tick, or for the same reasons as
	/// <see cref="Start(IEnumerator, CoroutineScope, CoroutineOptions, string, int, string)"/>:
	/// <paramref name="scope"/> is a cancelled scope or registering the coroutine handle in
	/// the scope failed.
	/// </exception>
	/// <remarks>
	/// Coroutines can only be started when the scheduler is idle; as such, to create
	/// a coroutine during a tick and defer its first execution to the next tick, use
	/// <see cref="Start(IEnumerator, CoroutineScope, CoroutineOptions, string, int, string)"/> instead.
	/// </remarks>
	public CoroutineHandle StartImmediately(IEnumerator routine, CoroutineScope scope, CoroutineOptions? options = null,
			[CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0, [CallerMemberName] string callerMember = "") {
		if (ticking)
			throw new InvalidOperationException("StartImmediately() cannot be called in the middle of a scheduler tick; use Start() to defer execution to the next tick");
		return start(routine, scope, defer: false, options, callerFile, callerLine, callerMember);
	}

	internal bool TryCancel(CoroutineHandle handle, CoroCancellationReason reason) {
		if (!tryGetInstance(handle, out CoroutineInstance? inst))
			return false;
		if (inst.Status != CoroutineStatus.Running && inst.Status != CoroutineStatus.Paused)
			return false;
		inst.PendingCancellationReason ??= reason;
		if (!ticking)
			applyPending(inst);
		return true;
	}

	/// <summary>
	/// Requests cancellation of a live coroutine.
	/// </summary>
	/// <param name="handle">The handle for the coroutine to cancel.</param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="handle"/> refers to a known
	/// coroutine that is currently running or paused and the cancellation
	/// request was accepted; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// When in the middle of a coroutine step, cancellation is deferred to be
	/// applied at a safe point instead of interrupting execution immediately.
	/// <see cref="CoroCancellationReason.ManualStop"/> is recorded as the cancellation reason.
	/// </remarks>
	public bool TryCancel(CoroutineHandle handle) => TryCancel(handle, CoroCancellationReason.ManualStop);

	/// <summary>
	/// Requests that a running coroutine be paused.
	/// </summary>
	/// <param name="handle">The handle for the coroutine to pause.</param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="handle"/> refers to a known
	/// coroutine that is currently running and the pause request was accepted;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// When in the middle of a coroutine step, pausing is deferred to be applied
	/// at a safe point instead of interrupting execution immediately. While paused,
	/// the coroutine does not advance and its current wait does not progress.
	/// </remarks>
	public bool TryPause(CoroutineHandle handle) {
		if (!tryGetInstance(handle, out CoroutineInstance? inst))
			return false;
		if (inst.Status != CoroutineStatus.Running)
			return false;
		inst.PendingControl = PendingControlAction.Pause;
		if (!ticking)
			applyPending(inst);
		return true;
	}

	/// <summary>
	/// Requests that a paused coroutine be resumed.
	/// </summary>
	/// <param name="handle">The handle for the coroutine to resume.</param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="handle"/> refers to a known
	/// coroutine that is currently paused and the resume request was accepted;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// When in the middle of a coroutine step, resuming is deferred to be applied
	/// at a safe point instead of interrupting execution immediately.
	/// </remarks>
	public bool TryResume(CoroutineHandle handle) {
		if (!tryGetInstance(handle, out CoroutineInstance? inst))
			return false;
		if (inst.Status != CoroutineStatus.Paused)
			return false;
		inst.PendingControl = PendingControlAction.Resume;
		if (!ticking)
			applyPending(inst);
		return true;
	}

	/// <summary>
	/// Retrieves the current or terminal info record for a coroutine handle.
	/// </summary>
	/// <param name="handle">The handle to inspect.</param>
	/// <param name="info">
	/// On success, a snapshot of the coroutine's current state if it is still live,
	/// or its terminal record if it has already completed, been cancelled, or faulted.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="handle"/> still identifies a live
	/// coroutine or a retained terminal record; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// Terminal info remains queryable after the coroutine has finished and
	/// continues to be until the handle is invalidated, typically when the internal
	/// slot is reused for a newer coroutine.
	/// </remarks>
	public bool TryGetInfo(CoroutineHandle handle, out CoroutineInfo info) {
		if (!tryGetSlot(handle, out CoroutineSlot? slot)) {
			info = default;
			return false;
		}
		if (slot.Instance is not null) {
			info = makeInfo(slot.Instance);
			return true;
		}
		info = slot.TerminalInfo ?? default;
		return slot.TerminalInfo is not null;
	}

	/// <summary>
	/// Advances all active coroutines one scheduler tick.
	/// </summary>
	/// <param name="dt">The scaled deltatime for this tick.</param>
	/// <param name="rawDt">The unscaled deltatime for this tick.</param>
	/// <param name="phase">The update phase associated with this tick.</param>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Tick(double, double, CoroUpdatePhase)"/> is called while
	/// a previous call is still in progress.
	/// </exception>
	/// <remarks>
	/// Each call represents one scheduler tick, regardless of the passed parameter values.
	/// Coroutines started during this call are activated after the tick completes and do
	/// not run until the next tick. Pending control operations (cancel/pause/resume) are
	/// applied before the tick fully completes.
	/// </remarks>
	public void Tick(double dt, double rawDt, CoroUpdatePhase phase) {
		if (ticking)
			throw new InvalidOperationException("Tick() is not reentrant and cannot be called while a previous call is in progress");
		ticking = true;
		tick++;
		int cnt = activeSlots.Count;
		try {
			for (int i = 0; i < cnt; i++) {
				CoroutineInstance? inst = slots[activeSlots[i]].Instance;
				if (inst is not null && inst.Status == CoroutineStatus.Running)
					runInstance(inst, dt, rawDt, phase);
			}
		} finally {
			for (int i = 0; i < cnt; i++) {
				CoroutineInstance? inst = slots[activeSlots[i]].Instance;
				if (inst is not null)
					applyPending(inst);
			}
			reapAll();
			ticking = false;
			commitPendingActivation();
		}
		processUnhandledFaults();
	}

	/// <summary>
	/// Returns a snapshot of info records for all currently active coroutines.
	/// </summary>
	/// <returns>
	/// An array containing a <see cref="CoroutineInfo"/> record for each
	/// currently active coroutine.
	/// </returns>
	/// <remarks>
	/// The returned records describe the scheduler state at the moment the snapshot is taken.
	/// Terminal records are not included; use <see cref="TryGetInfo(CoroutineHandle, out CoroutineInfo)"/>
	/// to get information for a specific completed/cancelled/faulted coroutine by handle.
	/// </remarks>
	public CoroutineInfo[] SnapshotActive() {
		List<CoroutineInfo> ret = new List<CoroutineInfo>(activeSlots.Count);
		for (int i = 0; i < activeSlots.Count; i++) {
			CoroutineInstance? inst = slots[activeSlots[i]].Instance;
			if (inst is not null)
				ret.Add(makeInfo(inst));
		}
		return ret.ToArray();
	}

	/// <summary>
	/// Retrieves a stacktrace for a live coroutine or retained terminal record.
	/// </summary>
	/// <param name="handle">The handle to inspect.</param>
	/// <param name="trace">
	/// On success, a stacktrace of the coroutine, including any available debug names or
	/// source metadata.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="handle"/> still identifies a live
	/// coroutine or a retained terminal record; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// Terminal traces remain queryable after the coroutine has finished and
	/// continue to be until the handle is invalidated, typically when the internal
	/// slot is reused for a newer coroutine.
	/// </remarks>
	public bool TryGetTrace(CoroutineHandle handle, [NotNullWhen(true)] out CoroutineTrace? trace) {
		trace = null;
		if (!tryGetSlot(handle, out CoroutineSlot? slot))
			return false;
		if (slot.Instance is CoroutineInstance inst) {
			trace = makeTrace(inst);
			return true;
		}
		if (slot.TerminalTrace is not null) {
			trace = slot.TerminalTrace;
			return true;
		}
		return false;
	}

	// ==========================================================================
	// internal api
	internal bool TryRetainHandle(CoroutineHandle handle) {
		if (!tryGetSlot(handle, out CoroutineSlot? slot))
			return false;
		slot.RetainCount++;
		return true;
	}

	internal void ReleaseRetainedHandle(CoroutineHandle handle) {
		if (!tryGetSlot(handle, out CoroutineSlot? slot))
			return;
		if (slot.RetainCount < 0)
			throw new InternalStateException($"slot has invalid RetainCount value of {slot.RetainCount}");
		if (slot.RetainCount == 0)
			throw new InternalStateException($"slot already has RetainCount == 0, so -1 for releasing handle {handle} would be invalid");
		slot.RetainCount--;
		if (slot.RetainCount == 0 && slot.Instance is null)
			freeSlots.Enqueue(handle.Slot);
	}

	// ==========================================================================
	// coroutine info/metadata creation
	private CoroutineHandle makeHandle() {
		while (freeSlots.Count > 0) {
			int idx = freeSlots.Dequeue();
			CoroutineSlot slot = slots[idx];
			if (slot.Instance is not null || slot.RetainCount != 0)
				continue;
			slot.Generation = Math.Max(slot.Generation + 1, 1); // clamp to >=1 to validate
			slot.TerminalInfo = null;
			slot.TerminalTrace = null;
			return new CoroutineHandle(idx, slot.Generation);
		}
		slots.Add(new CoroutineSlot {
			Generation = 1,
			Instance = null,
			TerminalInfo = null,
			TerminalTrace = null,
			RetainCount = 0
		});
		return new CoroutineHandle(slots.Count - 1, 1);
	}

	private static string? getDebugWaitDesc(CoroutineInstance inst) {
		try {
			return inst.Wait?.GetDebugWaitDescription();
		} catch {
			return "<debug wait description getter threw>";
		}
	}

	private static CoroutineInfo makeInfo(CoroutineInstance inst) {
		string? desc = getDebugWaitDesc(inst);
		string? name = inst.Options?.Name;
		if (string.IsNullOrEmpty(name) && inst.StackDepth > 0)
			name = inst.StackPeek().DebugName;
		return new CoroutineInfo {
			Handle = inst.Handle,
			Name = name ?? "<no name info available>",
			Owner = inst.Options?.Owner,
			Scope = inst.Scope,
			Status = inst.Status,
			LastPhase = inst.LastPhase,
			StartTick = inst.StartTick,
			TerminalTick = inst.TerminalTick,
			StackDepth = inst.StackDepth,
			CurrentWaitDebugDescription = desc,
			Fault = inst.Fault,
			CancellationReason = inst.CancellationReason
		};
	}

	private static CoroutineTrace makeTrace(CoroutineInstance inst) {
		CoroutineTraceFrame[] frames = new CoroutineTraceFrame[inst.StackDepth];
		for (int i = 0; i < inst.StackDepth; i++) {
			CoroutineStackFrame frame = inst.StackFrames[i];
			frames[i] = new CoroutineTraceFrame {
				DebugName = frame.DebugName,
				EnumeratorTypeName = frame.Enumerator.GetType().FullName ?? "<null>",
				SourceFile = frame.SourceFile,
				SourceLine = frame.SourceLine,
				SourceMember = frame.SourceMember
			};
		}
		string? desc = getDebugWaitDesc(inst);
		string? name = inst.Options?.Name;
		if (string.IsNullOrEmpty(name) && inst.StackDepth > 0)
			name = inst.StackPeek().DebugName;
		return new CoroutineTrace {
			Handle = inst.Handle,
			Name = name,
			ScopeName = inst.Scope?.Name,
			CurrentWaitDebugDescription = desc,
			Frames = frames
		};
	}

	private static string? enumDebugName(IEnumerator e) =>
		(e is NamedEnumerator named) ? named.DebugName : null;
	private static string? enumSourceFile(IEnumerator e) =>
		(e is NamedEnumerator named) ? named.SourceFile : null;
	private static int? enumSourceLine(IEnumerator e) =>
		(e is NamedEnumerator named) ? named.SourceLine : null;
	private static string? enumSourceMember(IEnumerator e) =>
		(e is NamedEnumerator named) ? named.SourceMember : null;

	// ==========================================================================
	// coroutine get/lifecycle
	private bool tryGetSlot(CoroutineHandle handle, [NotNullWhen(true)] out CoroutineSlot? slot) {
		slot = null;
		if (!handle.IsValid)
			return false;
		if (handle.Slot < 0 || handle.Slot >= slots.Count)
			return false;
		slot = slots[handle.Slot];
		if (slot.Generation != handle.Generation)
			return false;
		if (slot.Instance is not null)
			return true;
		if (slot.TerminalInfo is CoroutineInfo info && info.Handle == handle)
			return true;
		slot = null;
		return false;
	}

	private bool tryGetInstance(CoroutineHandle handle, [NotNullWhen(true)] out CoroutineInstance? inst) {
		inst = null;
		if (!handle.IsValid)
			return false;
		if (handle.Slot < 0 || handle.Slot >= slots.Count)
			return false;
		CoroutineSlot slot = slots[handle.Slot];
		if (slot.Generation != handle.Generation)
			return false;
		inst = slot.Instance;
		return slot.Instance is not null;
	}

	private void applyPending(CoroutineInstance inst) {
		if (inst.PendingCancellationReason is CoroCancellationReason reason) {
			inst.PendingCancellationReason = null;
			instCancel(inst, reason);
			return;
		}

		switch (inst.PendingControl) {
		case PendingControlAction.Pause:
			inst.PendingControl = PendingControlAction.None;
			if (inst.Status == CoroutineStatus.Running)
				inst.Status = CoroutineStatus.Paused;
			break;
		case PendingControlAction.Resume:
			inst.PendingControl = PendingControlAction.None;
			if (inst.Status == CoroutineStatus.Paused)
				inst.Status = CoroutineStatus.Running;
			break;
		}
	}

	private void requestReap(int slotidx) {
		pendingReap.Add(slotidx);
		if (!ticking)
			reapAll();
	}

	private void reapAll() {
		if (pendingReap.Count == 0)
			return;
		foreach (int slotidx in pendingReap) {
			if (slotidx < 0 || slotidx >= slots.Count)
				throw new InternalStateException($"pending-reap queue contains invalid slot index {slotidx}");
			CoroutineSlot slot = slots[slotidx];
			CoroutineInstance? inst = slot.Instance;
			if (inst is null)
				continue;
			slot.Instance = null;
			slot.TerminalInfo = makeInfo(inst);
			slot.TerminalTrace = inst.PreservedTerminalTrace ?? makeTrace(inst);
			activeSlots.Remove(slotidx);
			if (slot.RetainCount == 0)
				freeSlots.Enqueue(slotidx);
		}
		pendingReap.Clear();
	}

	private void commitPendingActivation() {
		if (pendingActivation.Count == 0)
			return;
		foreach (int slotidx in pendingActivation)
			activeSlots.Add(slotidx);
		pendingActivation.Clear();
	}

	// ==========================================================================
	// coroutine execution
	private static bool tryInterpretYield(object? yielded, in CoroutineContext ctx, [NotNullWhen(true)] out ICoroutineWait? wait, [NotNullWhen(false)] out Exception? ex) {
		wait = null;
		ex = null;
		if (yielded is null) {
			wait = new CoroWaitUntilTick(ctx.Tick + (CoroutineTick)1);
		} else if (yielded is ICoroutineWait cw) {
			wait = cw;
		} else if (yielded is CoroutineHandle handle) {
			if (handle == ctx.Handle) {
				wait = null;
				ex = new InvalidOperationException($"coroutine {ctx.Handle} tried to wait on its own handle");
				return false;
			}
			wait = new CoroWaitForHandle(handle, propagateFault: true, throwOnChildCancelled: false);
		} else {
			ex = new InvalidOperationException($"unsupported coroutine yield type '{yielded.GetType().FullName}'");
		}
		return wait is not null;
	}

	private void runInstance(CoroutineInstance inst, double dt, double rawDt, CoroUpdatePhase phase) {
		int steps = 0;

		for (;;) {
			applyPending(inst);
			if (inst.Status != CoroutineStatus.Running)
				return;
			if (inst.Scope is null)
				throw new InternalStateException("expected coro instance scope to be nonnull");
			inst.LastPhase = phase;
			CoroutineContext ctx = new CoroutineContext(this, inst.Handle, inst.Scope, dt, rawDt, phase, tick);
			if (inst.Wait is not null) {
				bool shouldWait;
				try {
					shouldWait = inst.Wait.KeepWaiting(in ctx);
				} catch (Exception ex) {
					instFault(inst, ex);
					return;
				}
				if (shouldWait)
					return;
				inst.ClearWait(this);
			}
			if (inst.StackDepth == 0) {
				instComplete(inst);
				return;
			}
			if (++steps > inst.Options.MaxStepsPerTick) {
				if (!inst.TrySetWait(this, new CoroWaitUntilTick(tick + (CoroutineTick)1), out Exception? setWaitEx))
					instFault(inst, setWaitEx);
				return;
			}

			CoroutineStackFrame frame = inst.StackPeek();
			bool moved;
			object? yielded;
			try {
				moved = frame.Enumerator.MoveNext();
				yielded = moved ? frame.Enumerator.Current : null;
			} catch (Exception ex) {
				instFault(inst, ex);
				return;
			}
			if (!moved) {
				inst.StackPopAndDispose();
				continue;
			}
			if (yielded is IEnumerator child) {
				inst.StackPush(child,
					enumDebugName(child) ?? CoroNameCleanup.Clean(child.GetType().Name),
					enumSourceFile(child) ?? "",
					enumSourceLine(child) ?? 0,
					enumSourceMember(child) ?? "");
				continue;
			}

			if (!tryInterpretYield(yielded, in ctx, out ICoroutineWait? newWait, out Exception? interpretEx)) {
				instFault(inst, interpretEx);
				return;
			}
			if (!inst.TrySetWait(this, newWait, out Exception? setWaitEx2))
				instFault(inst, setWaitEx2);
			return;
		}
	}

	private void instComplete(CoroutineInstance inst) {
		inst.TransitionToCompleted(tick);
		requestReap(inst.Handle.Slot);
	}

	private void instCancel(CoroutineInstance inst, CoroCancellationReason reason) {
		// capture stacktrace here before the unwind
		inst.PreservedTerminalTrace = makeTrace(inst);

		inst.TransitionToCancelled(this, reason, tick);
		requestReap(inst.Handle.Slot);
	}

	private void instFault(CoroutineInstance inst, Exception ex) {
		// capture stacktrace here before the unwind
		inst.PreservedTerminalTrace = makeTrace(inst);

		inst.TransitionToFaulted(this, ex, tick);
		if (slots[inst.Handle.Slot].RetainCount == 0) { // TODO: better policy for what counts as "unhandled"
			pendingUnhandledFaults.Add(new CoroutineUnhandledFaultInfo {
				Exception = ex,
				Info = makeInfo(inst),
				Trace = inst.PreservedTerminalTrace
			});
		}
		requestReap(inst.Handle.Slot);
	}

	private void processUnhandledFaults() {
		if (pendingUnhandledFaults.Count == 0)
			return;
		try {
			for (int i = 0; i < pendingUnhandledFaults.Count; i++) {
				CoroutineUnhandledFaultInfo info = pendingUnhandledFaults[i];
				UnhandledFault?.Invoke(info);
				if (UnhandledFaultMode.Tag is CoroUnhandledFaultMode.Case.LogAfterTick or CoroUnhandledFaultMode.Case.LogAndThrowAfterTick)
					DiagnosticLogSink?.Invoke(CoroDiagnostics.FormatFault(info));
			}
			if (UnhandledFaultMode.Tag is CoroUnhandledFaultMode.Case.ThrowAfterTick or CoroUnhandledFaultMode.Case.LogAndThrowAfterTick) {
				CoroutineUnhandledFaultInfo[] arr = pendingUnhandledFaults.ToArray();
				throw new CoroutineUnhandledFaultsException(arr);
			}
		} finally {
			pendingUnhandledFaults.Clear();
		}
	}
}
