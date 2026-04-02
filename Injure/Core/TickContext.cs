// SPDX-License-Identifier: MIT

using Injure.Input;
using Injure.Timing;

namespace Injure.Core;

public readonly record struct TickCallbackInfo(
	PerfTick ScheduledAt,
	PerfTick ActualAt,
	PerfTick PreviousScheduledAt,
	PerfTick PreviousActualAt,
	PerfTick Period,
	PerfTick Elapsed,
	PerfTick Late
);

public readonly ref struct TickContext(in TickCallbackInfo timing, InputView input) {
	public TickCallbackInfo Timing { get; } = timing;
	public InputView Input { get; } = input;
}
