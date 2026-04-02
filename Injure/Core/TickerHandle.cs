// SPDX-License-Identifier: MIT

using System;

namespace Injure.Core;

public readonly struct TickerHandle(int slot, int generation) : IEquatable<TickerHandle> {
	public static readonly TickerHandle Invalid = default;
	public int Slot { get; } = slot;
	public int Generation { get; } = generation;
	public readonly bool IsValid => Generation > 0;

	public bool Equals(TickerHandle other) => Slot == other.Slot && Generation == other.Generation;
	public override bool Equals(object? obj) => obj is TickerHandle other && Equals(other);
	public override int GetHashCode() => unchecked((Slot * 397) ^ Generation);
	public static bool operator ==(TickerHandle left, TickerHandle right) => left.Equals(right);
	public static bool operator !=(TickerHandle left, TickerHandle right) => !left.Equals(right);
}
