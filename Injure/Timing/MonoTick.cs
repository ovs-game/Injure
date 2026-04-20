// SPDX-License-Identifier: MIT

using System;

using Injure.Analyzers.Attributes;
using Injure.Core;

namespace Injure.Timing;

[StronglyTypedInt(typeof(ulong))]
public readonly partial struct MonoTick {
	public static readonly MonoTick Frequency = (MonoTick)1000000000;
	public static MonoTick GetCurrent() => SDLOwner.MonoTickGetCurrent();

	public static MonoTick PeriodFromHz(double hz) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hz);
		ulong ticks = checked((ulong)Math.Round(Frequency.Value / hz, MidpointRounding.AwayFromZero));
		return (MonoTick)Math.Max(ticks, 1);
	}
	public double ToSeconds() => (double)Value / Frequency.Value;
}
