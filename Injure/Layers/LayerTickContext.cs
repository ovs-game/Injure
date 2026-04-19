// SPDX-License-Identifier: MIT

using Injure.Input;
using Injure.Timing;

namespace Injure.Layers;

public readonly ref struct LayerTickContext(MonoTick monoTick, double dt, double rawDt, double time, double rawTime, ulong tickNum, InputView input) {
	public MonoTick MonoTick { get; } = monoTick;
	public double DeltaTime { get; } = dt;
	public double RawDeltaTime { get; } = rawDt;
	public double Time { get; } = time;
	public double RawTime { get; } = rawTime;
	public ulong TickNum { get; } = tickNum;

	public InputView Input { get; } = input;
}
