// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Injure.Timing;

namespace Injure.Core;

internal sealed class ScheduledTicker {
	private readonly TickerOptions options;
	private TickerTiming timing;

	private bool hadCallback;
	private PerfTick lastScheduledAt;
	private PerfTick lastActualAt;
	private uint lastBatchID;
	private int runsThisBatch;

	public PerfTick NextAt { get; private set; }
	public int Priority => options.Priority;
	public ulong InsertionOrder { get; private set; } // tie breaker for deterministic sorting of equal ones
	internal event TickerCallback? CallbackEv;

	public ScheduledTicker(in TickerSpec spec) {
		if (spec.Timing.Period == PerfTick.Zero)
			throw new ArgumentOutOfRangeException(nameof(spec), "period must be nonzero");
		if (spec.Options.OverrunMode == TickerOverrunMode.CatchUp)
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spec.Options.MaxBurst);
		options = spec.Options;
		timing = spec.Timing;

		hadCallback = false;
		lastScheduledAt = PerfTick.Zero;
		lastActualAt = PerfTick.Zero;
		lastBatchID = 0;
		runsThisBatch = 0;
		NextAt = PerfTick.Zero;
		InsertionOrder = 0;
	}

	public void Activate(PerfTick commitAt, ulong insertionOrder) {
		hadCallback = false;
		lastScheduledAt = PerfTick.Zero;
		lastActualAt = PerfTick.Zero;
		lastBatchID = 0;
		runsThisBatch = 0;
		NextAt = options.StartMode switch {
			TickerStartMode.FromCommitTime => commitAt + timing.InitialOffset,
			TickerStartMode.AtAbsoluteTick => options.StartAt,
			_ => throw new UnreachableException()
		};
		InsertionOrder = insertionOrder;
	}

	public void Retime(PerfTick commitAt, in TickerTiming tm, TickerRetimingMode mode) {
		if (tm.Period == PerfTick.Zero)
			throw new ArgumentOutOfRangeException(nameof(tm), "period must be nonzero");

		PerfTick oldPeriod = timing.Period;
		PerfTick oldNextAt = NextAt;
		timing = tm;
		switch (mode) {
		case TickerRetimingMode.KeepPhase:
			hadCallback = false;
			lastScheduledAt = PerfTick.Zero;
			lastActualAt = PerfTick.Zero;
			lastBatchID = 0;
			runsThisBatch = 0;
			NextAt = commitAt + timing.InitialOffset;
			break;
		case TickerRetimingMode.RestartFromCommitTime:
			if (oldPeriod == PerfTick.Zero)
				throw new InternalStateException("oldPeriod is somehow zero, this should've been rejected earlier");
			if (oldNextAt > commitAt) {
				PerfTick rem = oldNextAt - commitAt;
				UInt128 newrem128 = ((UInt128)rem.Value * (UInt128)timing.Period.Value) / (UInt128)oldPeriod.Value;
				NextAt = commitAt + checked((PerfTick)(ulong)newrem128);
			} else {
				NextAt = commitAt;
			}
			break;
		}
	}

	public bool TryRunOneIfDue(PerfTick now, uint batchID) {
		if (now < NextAt)
			return false;
		switch (options.OverrunMode) {
		case TickerOverrunMode.CatchUp:
			if (lastBatchID != batchID) {
				lastBatchID = batchID;
				runsThisBatch = 0;
			}
			if (runsThisBatch >= options.MaxBurst)
				return false;
			doCallback(NextAt, now);
			NextAt += timing.Period;
			runsThisBatch++;
			return true;
		case TickerOverrunMode.Once:
			PerfTick scheduledAt = NextAt;
			doCallback(scheduledAt, now);
			PerfTick missed = (now - scheduledAt) / timing.Period;
			NextAt = scheduledAt + checked(timing.Period * (missed + (PerfTick)1));
			return true;
		default:
			throw new UnreachableException();
		}
	}

	private void doCallback(PerfTick scheduledAt, PerfTick actualAt) {
		PerfTick previousScheduledAt = hadCallback ? lastScheduledAt : scheduledAt - timing.Period;
		PerfTick previousActualAt = hadCallback ? lastActualAt : actualAt - timing.Period;
		PerfTick elapsed = hadCallback ? actualAt - lastActualAt : timing.Period;
		PerfTick late = actualAt >= scheduledAt ? actualAt - scheduledAt : PerfTick.Zero;
		CallbackEv?.Invoke(new TickCallbackInfo(
			ScheduledAt: scheduledAt,
			ActualAt: actualAt,
			PreviousScheduledAt: previousScheduledAt,
			PreviousActualAt: previousActualAt,
			Period: timing.Period,
			Elapsed: elapsed,
			Late: late
		));
		lastScheduledAt = scheduledAt;
		lastActualAt = actualAt;
		hadCallback = true;
	}
}

public readonly record struct TickerSchedulerOptions(
	int BatchCallLimit = 64,
	int EventPollInterval = 8,
	PerfTick MaxBatchDuration = default
);

// not thread safe
public sealed class TickerScheduler(in TickerSchedulerOptions options) : ITickerRegistry {
	private enum TickerSlotState {
		Empty,
		PendingAdd,
		Active
	}
	private sealed class TickerSlot {
		public required int Generation;
		public required TickerSlotState State;
		public required ScheduledTicker Scheduled {
			get {
				if (State == TickerSlotState.Empty)
					throw new InternalStateException("empty ticker slot has no scheduled val");
				return field;
			}
			set;
		}
	}

	private enum TickerCommandKind {
		Add,
		Remove,
		Retime
	}
	private readonly record struct TickerCommand(
		TickerCommandKind Kind,
		TickerHandle Handle,
		TickerTiming Timing = default,
		TickerRetimingMode RetimingMode = default
	);

	private readonly TickerSchedulerOptions options = options;
	private readonly List<TickerSlot> slots = new List<TickerSlot>();
	private readonly List<int> activeSlots = new List<int>();
	private readonly List<TickerCommand> pending = new List<TickerCommand>();
	private ulong nextInsertionOrder;
	private uint nextBatchID;

	public TickerHandle Add(in TickerSpec spec) {
		int slotidx = makeSlot();
		slots[slotidx].State = TickerSlotState.PendingAdd;
		slots[slotidx].Scheduled = new ScheduledTicker(in spec);
		TickerHandle handle = new(slotidx, slots[slotidx].Generation);
		pending.Add(new TickerCommand(TickerCommandKind.Add, handle));
		return handle;
	}

	public bool Remove(TickerHandle handle) {
		if (!tryGetSlot(handle, out int slotidx) || slots[slotidx].State == TickerSlotState.Empty)
			return false;
		pending.Add(new TickerCommand(TickerCommandKind.Remove, handle));
		return true;
	}

	public bool Retime(TickerHandle handle, in TickerTiming timing, TickerRetimingMode mode = TickerRetimingMode.KeepPhase) {
		if (!tryGetSlot(handle, out int slotidx) || slots[slotidx].State == TickerSlotState.Empty)
			return false;
		pending.Add(new TickerCommand(TickerCommandKind.Retime, handle, timing, mode));
		return true;
	}

	public bool Subscribe(TickerHandle handle, TickerCallback callback) {
		if (!tryGetSlot(handle, out int slotidx) || slots[slotidx].State == TickerSlotState.Empty)
			return false;
		slots[slotidx].Scheduled.CallbackEv += callback;
		return true;
	}

	public bool Unsubscribe(TickerHandle handle, TickerCallback callback) {
		if (!tryGetSlot(handle, out int slotidx) || slots[slotidx].State == TickerSlotState.Empty)
			return false;
		slots[slotidx].Scheduled.CallbackEv -= callback;
		return true;
	}

	public void ApplyPending() {
		PerfTick commitAt = PerfTick.GetCurrent();
		foreach (TickerCommand cmd in pending) {
			if (!tryGetSlot(cmd.Handle, out int slotIndex))
				continue;
			TickerSlot slot = slots[slotIndex];
			switch (cmd.Kind) {
			case TickerCommandKind.Add:
				if (slot.State != TickerSlotState.PendingAdd)
					break;
				slot.Scheduled.Activate(commitAt, nextInsertionOrder++);
				slot.State = TickerSlotState.Active;
				break;
			case TickerCommandKind.Remove:
				slot.State = TickerSlotState.Empty;
				break;
			case TickerCommandKind.Retime:
				if (slot.State == TickerSlotState.Empty)
					break;
				slot.Scheduled.Retime(commitAt, cmd.Timing, cmd.RetimingMode);
				break;
			}
		}
		pending.Clear();
		rebuildActiveSlots();
	}

	public void RunDueTickers() {
		uint batchID = ++nextBatchID;
		int calls = 0;
		PerfTick start = PerfTick.GetCurrent();
		for (;;) {
			rebuildActiveSlots();
			bool ranAny = false;
			foreach (TickerSlot slot in slots) {
				if (slot.State != TickerSlotState.Active)
					continue;
				PerfTick now = PerfTick.GetCurrent();
				if (!slot.Scheduled.TryRunOneIfDue(now, batchID))
					continue;
				ranAny = true;
				calls++;
				if (calls >= options.BatchCallLimit || (options.EventPollInterval > 0 && calls > options.EventPollInterval)) {
					rebuildActiveSlots();
					return;
				}
				if (options.MaxBatchDuration > PerfTick.Zero) {
					PerfTick elapsed = PerfTick.GetCurrent() - start;
					if (elapsed >= options.MaxBatchDuration) {
						rebuildActiveSlots();
						return;
					}
				}
			}
			if (!ranAny) {
				rebuildActiveSlots();
				return;
			}
		}
	}

	public bool TryGetEarliestNextAt(out PerfTick nextAt) {
		if (activeSlots.Count == 0) {
			nextAt = PerfTick.Zero;
			return false;
		}
		int firstSlot = activeSlots[0];
		nextAt = slots[firstSlot].Scheduled.NextAt;
		return true;
	}

	private int makeSlot() {
		for (int i = 0; i < slots.Count; i++) {
			if (slots[i].State != TickerSlotState.Empty)
				continue;
			slots[i].Generation++;
			slots[i].State = TickerSlotState.Empty;
			return i;
		}
		slots.Add(new TickerSlot { Generation = 1, State = TickerSlotState.Empty, Scheduled = null! });
		return slots.Count - 1;
	}

	private bool tryGetSlot(TickerHandle handle, out int slotidx) {
		slotidx = handle.Slot;
		if (slotidx < 0 || slotidx >= slots.Count)
			return false;
		return slots[slotidx].Generation == handle.Generation;
	}

	private void rebuildActiveSlots() {
		activeSlots.Clear();
		for (int i = 0; i < slots.Count; i++)
			if (slots[i].State == TickerSlotState.Active && slots[i].Scheduled is not null)
				activeSlots.Add(i);
		activeSlots.Sort((int a, int b) => {
			ScheduledTicker left = slots[a].Scheduled;
			ScheduledTicker right = slots[b].Scheduled;
			int cmp = left.NextAt.CompareTo(right.NextAt);
			if (cmp != 0)
				return cmp;
			cmp = left.Priority.CompareTo(right.Priority);
			if (cmp != 0)
				return cmp;
			cmp = left.InsertionOrder.CompareTo(right.InsertionOrder);
			if (cmp != 0)
				return cmp;
			return a.CompareTo(b);
		});
	}
}
