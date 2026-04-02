// SPDX-License-Identifier: MIT

using Injure.Input;

namespace Injure.Layers;

public readonly ref struct LayerTickContext(double dt, double rawDt, double time, double rawTime, ulong tickNum, InputView input) {
	public double DeltaTime { get; } = dt;
	public double RawDeltaTime { get; } = rawDt;
	public double Time { get; } = time;
	public double RawTime { get; } = rawTime;
	public ulong TickNum { get; } = tickNum;

	public InputView Input { get; } = input;
}
