// SPDX-License-Identifier: MIT

using System;

namespace Injure.Coroutines;

public readonly struct CoroutineHandle(int slot, int generation) : IEquatable<CoroutineHandle> {
	public static readonly CoroutineHandle Invalid = default;
	public int Slot { get; } = slot;
	public int Generation { get; } = generation;
	public readonly bool IsValid => Generation > 0;

	public bool Equals(CoroutineHandle other) => Slot == other.Slot && Generation == other.Generation;
	public override bool Equals(object? obj) => obj is CoroutineHandle other && Equals(other);
	public override int GetHashCode() => unchecked((Slot * 397) ^ Generation);
	public static bool operator ==(CoroutineHandle left, CoroutineHandle right) => left.Equals(right);
	public static bool operator !=(CoroutineHandle left, CoroutineHandle right) => !left.Equals(right);
}
