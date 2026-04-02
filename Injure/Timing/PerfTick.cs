// SPDX-License-Identifier: MIT

using System;

using Injure.SDL;
using Injure.SourceGen;

namespace Injure.Timing;

[StronglyTypedInt(typeof(ulong))]
public readonly partial struct PerfTick {
	public static PerfTick Frequency => SDLOwner.PerfTickFrequency;
	public static PerfTick GetCurrent() => SDLOwner.PerfTickGetCurrent();

	public static PerfTick PeriodFromHz(double hz) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hz);
		ulong ticks = checked((ulong)Math.Round(Frequency.Value / hz, MidpointRounding.AwayFromZero));
		return (PerfTick)Math.Max(ticks, 1);
	}
	public double ToSeconds() => (double)Value / Frequency.Value;
}
