// SPDX-License-Identifier: MIT

using Injure.Timing;

namespace Injure.Core;

public readonly record struct TickerTiming(PerfTick Period, PerfTick InitialOffset = default);

public enum TickerOverrunMode {
	CatchUp,
	Once
}

public enum TickerStartMode {
	FromCommitTime,
	AtAbsoluteTick
}

public enum TickerRetimingMode {
	KeepPhase,
	RestartFromCommitTime
}

public readonly record struct TickerOptions(
	int Priority,
	int MaxBurst,
	TickerOverrunMode OverrunMode,
	TickerStartMode StartMode,
	PerfTick StartAt
) {
	public static readonly TickerOptions Default = new TickerOptions(
		Priority: 0,
		MaxBurst: 8,
		OverrunMode: TickerOverrunMode.Once,
		StartMode: TickerStartMode.FromCommitTime,
		StartAt: default
	);
}

public readonly record struct TickerSpec(
	TickerTiming Timing,
	TickerOptions Options
);
