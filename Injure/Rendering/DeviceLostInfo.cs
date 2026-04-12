// SPDX-License-Identifier: MIT

using System;

namespace Injure.Rendering;

public enum DeviceState {
	Alive,
	Lost,
	Disposed
}

public enum DeviceLossInfoKind {
	Provisional,
	Final
}

public enum DeviceLossEventReason {
	_Invalid = 0,
	Unknown = 1,
	Destroyed = 2,
	InstanceDropped = 3,
	FailedCreation = 4,
	SurfaceAcquireDeviceLost = 5
}

public sealed record DeviceLostInfo(
	DeviceLossInfoKind Kind,
	DeviceLossEventReason Reason,
	string? Message
);

public sealed class DeviceLostException(DeviceLostInfo info) : Exception($"reason: {info.Reason}, message: {info.Message ?? "<no message provided>"}") {
	public DeviceLostInfo Info { get; } = info;
}
