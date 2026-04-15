// SPDX-License-Identifier: MIT

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
