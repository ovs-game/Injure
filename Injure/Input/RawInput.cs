// SPDX-License-Identifier: MIT

using System;

using Injure.Timing;

namespace Injure.Input;

public enum InputDeviceType {
	Keyboard
}

public enum EdgeType {
	Press,
	Release
}

public readonly struct RawInputID(InputDeviceType type, int deviceID, int code) : IEquatable<RawInputID> {
	public readonly InputDeviceType Type = type;
	public readonly int DeviceID = deviceID;
	public readonly int Code = code;

	public bool Equals(RawInputID other) => Type == other.Type && DeviceID == other.DeviceID && Code == other.Code;
	public override bool Equals(object? obj) => obj is RawInputID other && Equals(other);
	public override int GetHashCode() => HashCode.Combine((int)Type, DeviceID, Code);
	public static bool operator ==(RawInputID left, RawInputID right) => left.Equals(right);
	public static bool operator !=(RawInputID left, RawInputID right) => !left.Equals(right);
}

public readonly struct RawInputEvent(RawInputID id, EdgeType edge, PerfTick perfTimestamp) {
	public readonly RawInputID ID = id;
	public readonly EdgeType Edge = edge;
	public readonly PerfTick PerfTimestamp = perfTimestamp;
}
