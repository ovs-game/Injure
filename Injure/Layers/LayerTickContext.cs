// SPDX-License-Identifier: MIT

using Injure.Input;
using Injure.Scheduling;
using Injure.Timing;

namespace Injure.Layers;

public readonly ref struct LayerTickContext(TickCallbackInfo tickInfo, double dt, double rawDt, double time, double rawTime, ulong tickNum, InputView input, ControlView controls) {
	public TickCallbackInfo TickInfo { get; } = tickInfo;
	public MonoTick Tick => TickInfo.ActualAt;
	public MonoTick ScheduledTick => TickInfo.ScheduledAt;

	public double DeltaTime { get; } = dt;
	public double RawDeltaTime { get; } = rawDt;
	public double Time { get; } = time;
	public double RawTime { get; } = rawTime;
	public ulong TickNum { get; } = tickNum;

	public InputView Input { get; } = input;
	public ControlView Controls { get; } = controls;

	public ActionStateView Actions => Controls.Actions;
	public PointerState Pointer => Controls.Pointer;
}
