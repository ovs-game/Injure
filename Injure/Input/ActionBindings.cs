// SPDX-License-Identifier: MIT

using System.Numerics;

using Injure.Analyzers.Attributes;

namespace Injure.Input;

public readonly record struct ButtonBinding(
	ActionID Action,
	InputButtonSource Source
);

public readonly record struct StateAxisBinding(
	ActionID Action,
	InputStateAxisSource Source,
	AxisDeadzone Deadzone,
	float Scale
);

public readonly record struct StateAxis2DBinding(
	ActionID Action,
	InputStateAxis2DSource Source,
	Axis2DDeadzone Deadzone,
	Vector2 Scale
);

public readonly record struct ImpulseAxisBinding(
	ActionID Action,
	InputImpulseAxisSource Source,
	float Scale
);

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct StateAxisMergePolicy {
	public enum Case {
		MaxAbs = 1,
		SumClamp,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct StateAxis2DMergePolicy {
	public enum Case {
		MaxMagnitude = 1,
		SumClamp,
	}
}
