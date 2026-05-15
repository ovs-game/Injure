// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

namespace Injure.ModKit.Abstractions.MonoMod;

public readonly struct HookTarget : IEquatable<HookTarget> {
	public required string ID { get; init; }
	public required MethodBase Method { get; init; }
	public required Type OrigDelegateType { get; init; }

	public bool Equals(HookTarget other) => ID == other.ID && Method == other.Method && OrigDelegateType == other.OrigDelegateType;
	public override bool Equals(object? obj) => obj is HookTarget other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(ID, Method, OrigDelegateType);
	public static bool operator ==(HookTarget left, HookTarget right) => left.Equals(right);
	public static bool operator !=(HookTarget left, HookTarget right) => !left.Equals(right);
}
