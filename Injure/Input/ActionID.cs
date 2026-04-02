// SPDX-License-Identifier: MIT

using System;

namespace Injure.Input;

public readonly struct ActionID : IEquatable<ActionID> {
	internal readonly int Val;
	internal ActionID(int val) => Val = val;

	public bool Equals(ActionID other) => Val == other.Val;
	public override bool Equals(object? obj) => obj is ActionID other && Equals(other);
	public override int GetHashCode() => Val;
	public static bool operator ==(ActionID left, ActionID right) => left.Val == right.Val;
	public static bool operator !=(ActionID left, ActionID right) => left.Val != right.Val;
}
