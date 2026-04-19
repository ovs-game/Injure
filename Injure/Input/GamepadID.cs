// SPDX-License-Identifier: MIT

using System;

namespace Injure.Input;

public readonly struct GamepadID : IEquatable<GamepadID> {
	internal readonly uint Value; // 0 is invalid
	internal GamepadID(uint value) => Value = value;

	public bool Equals(GamepadID other) => Value == other.Value;
	public override bool Equals(object? obj) => obj is GamepadID other && Equals(other);
	public override int GetHashCode() => unchecked((int)Value);
	public static bool operator ==(GamepadID left, GamepadID right) => left.Value == right.Value;
	public static bool operator !=(GamepadID left, GamepadID right) => left.Value != right.Value;
}
