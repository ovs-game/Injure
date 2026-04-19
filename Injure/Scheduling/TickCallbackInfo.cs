// SPDX-License-Identifier: MIT

using Injure.Timing;

namespace Injure.Scheduling;

public readonly record struct TickCallbackInfo(
	MonoTick ScheduledAt,
	MonoTick ActualAt,
	MonoTick PreviousScheduledAt,
	MonoTick PreviousActualAt,
	MonoTick Period,
	MonoTick Elapsed,
	MonoTick Late
);
