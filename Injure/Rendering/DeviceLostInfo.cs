// SPDX-License-Identifier: MIT

using System;

using Injure.Analyzers.Attributes;

namespace Injure.Rendering;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct DeviceState {
	public enum Case {
		Alive = 1,
		Lost,
		Disposed
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct DeviceLossInfoKind {
	public enum Case {
		Provisional = 1,
		Final
	}
}

[ClosedEnum]
public readonly partial struct DeviceLossEventReason {
	public enum Case {
		Unknown,
		Destroyed,
		InstanceDropped,
		FailedCreation,
		SurfaceAcquireDeviceLost
	}
}

public sealed record DeviceLostInfo(
	DeviceLossInfoKind Kind,
	DeviceLossEventReason Reason,
	string? Message
);

public sealed class DeviceLostException(DeviceLostInfo info) : Exception($"reason: {info.Reason}, message: {info.Message ?? "<no message provided>"}") {
	public DeviceLostInfo Info { get; } = info;
}
