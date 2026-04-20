// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;
using Injure.Timing;

namespace Injure.Scheduling;

public readonly record struct TickerTiming(MonoTick Period, MonoTick InitialOffset = default);

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TickerOverrunMode {
	public enum Case {
		CatchUp = 1,
		Once
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TickerStartMode {
	public enum Case {
		FromCommitTime = 1,
		AtAbsoluteTick
	}
}

[ClosedEnum(CheckZeroName = false)]
public readonly partial struct TickerRetimingMode {
	public enum Case {
		KeepPhase,
		RestartFromCommitTime
	}
}

public readonly record struct TickerOptions(
	int Priority,
	int MaxBurst,
	TickerOverrunMode OverrunMode,
	TickerStartMode StartMode,
	MonoTick StartAt
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
