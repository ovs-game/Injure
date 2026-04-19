// SPDX-License-Identifier: MIT

using Injure.Timing;

namespace Injure.Input;

public abstract record InputEvent(MonoTick Tick);

public sealed record KeyEvent(
	MonoTick Tick,
	Key Key,
	EdgeType Edge
) : InputEvent(Tick);

public sealed record GamepadAddedEvent(
	MonoTick Tick,
	GamepadID Gamepad
) : InputEvent(Tick);

public sealed record GamepadRemovedEvent(
	MonoTick Tick,
	GamepadID Gamepad
) : InputEvent(Tick);

public sealed record GamepadAxisEvent(
	MonoTick Tick,
	GamepadID Gamepad,
	GamepadAxis Axis,
	float Value // [-1, 1] for sticks, [0, 1] for triggers
) : InputEvent(Tick);

public sealed record GamepadButtonEvent(
	MonoTick Tick,
	GamepadID Gamepad,
	GamepadButton Button,
	EdgeType Edge
) : InputEvent(Tick);

public sealed record PointerMoveEvent(
	MonoTick Tick,
	float X,
	float Y,
	float DeltaX,
	float DeltaY
) : InputEvent(Tick);

public sealed record PointerButtonEvent(
	MonoTick Tick,
	PointerButton Button,
	EdgeType Edge,
	int Clicks,
	float X,
	float Y
) : InputEvent(Tick);

public sealed record PointerWheelEvent(
	MonoTick Tick,
	float X,
	float Y,
	int IntegerX,
	int IntegerY,
	float MouseX,
	float MouseY
) : InputEvent(Tick);

public sealed record TextEnteredEvent(
	MonoTick Tick,
	string Text
) : InputEvent(Tick);
