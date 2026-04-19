// SPDX-License-Identifier: MIT

using System.Numerics;

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

public enum StateAxisMergePolicy {
	MaxAbs,
	SumClamp
}

public enum StateAxis2DMergePolicy {
	MaxMagnitude,
	SumClamp
}
