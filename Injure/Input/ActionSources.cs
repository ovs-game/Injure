// SPDX-License-Identifier: MIT

using System;

namespace Injure.Input;

public enum InputButtonSourceKind {
	Key,
	GamepadButton,
	PointerButton
}

public readonly struct InputButtonSource : IEquatable<InputButtonSource> {
	private readonly Key key;
	private readonly PointerButton pointerButton;
	private readonly GamepadButton gamepadButton;

	public InputButtonSourceKind Kind { get; }
	public Key KeyValue => Kind == InputButtonSourceKind.Key ? key :
		throw new InvalidOperationException("input button source does not contain a key");
	public PointerButton PointerButtonValue => Kind == InputButtonSourceKind.PointerButton ? pointerButton :
		throw new InvalidOperationException("input button source does not contain a pointer button");
	public GamepadButton GamepadButtonValue => Kind == InputButtonSourceKind.GamepadButton ? gamepadButton :
		throw new InvalidOperationException("input button source does not contain a gamepad button");

	private InputButtonSource(InputButtonSourceKind kind, Key key, PointerButton pointerButton, GamepadButton gamepadButton) {
		Kind = kind;
		this.key = key;
		this.pointerButton = pointerButton;
		this.gamepadButton = gamepadButton;
	}

	public static InputButtonSource Key(Key key) =>
		new InputButtonSource(InputButtonSourceKind.Key, key, default, default);
	public static InputButtonSource PointerButton(PointerButton button) =>
		new InputButtonSource(InputButtonSourceKind.PointerButton, default, button, default);
	public static InputButtonSource GamepadButton(GamepadButton button) =>
		new InputButtonSource(InputButtonSourceKind.GamepadButton, default, default, button);

	public bool TryGetKey(out Key value) {
		if (Kind == InputButtonSourceKind.Key) {
			value = key;
			return true;
		}
		value = default;
		return false;
	}

	public bool TryGetPointerButton(out PointerButton value) {
		if (Kind == InputButtonSourceKind.PointerButton) {
			value = pointerButton;
			return true;
		}
		value = default;
		return false;
	}

	public bool TryGetGamepadButton(out GamepadButton value) {
		if (Kind == InputButtonSourceKind.GamepadButton) {
			value = gamepadButton;
			return true;
		}
		value = default;
		return false;
	}

	public bool Equals(InputButtonSource other) => Kind == other.Kind && key == other.key && pointerButton == other.pointerButton && gamepadButton == other.gamepadButton;
	public override bool Equals(object? obj) => obj is InputButtonSource other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Kind, key, pointerButton, gamepadButton);
	public static bool operator ==(InputButtonSource left, InputButtonSource right) => left.Equals(right);
	public static bool operator !=(InputButtonSource left, InputButtonSource right) => !left.Equals(right);
}

public readonly struct DigitalAxisSource : IEquatable<DigitalAxisSource> {
	public InputButtonSource Negative { get; }
	public InputButtonSource Positive { get; }
	public SOCDPolicy SOCD { get; }

	public DigitalAxisSource(InputButtonSource negative, InputButtonSource positive, SOCDPolicy socd = SOCDPolicy.Last) {
		if (negative == positive)
			throw new ArgumentException("negative and positive sources must be different");
		Negative = negative;
		Positive = positive;
		SOCD = socd;
	}

	public bool Equals(DigitalAxisSource other) => Negative == other.Negative && Positive == other.Positive && SOCD == other.SOCD;
	public override bool Equals(object? obj) => obj is DigitalAxisSource other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Negative, Positive, SOCD);
	public static bool operator ==(DigitalAxisSource left, DigitalAxisSource right) => left.Equals(right);
	public static bool operator !=(DigitalAxisSource left, DigitalAxisSource right) => !left.Equals(right);
}

public enum InputStateAxisSourceKind {
	GamepadAxis,
	DigitalPair
}

public readonly struct InputStateAxisSource : IEquatable<InputStateAxisSource> {
	private readonly GamepadAxis gamepadAxis;
	private readonly DigitalAxisSource digital;

	public InputStateAxisSourceKind Kind { get; }
	public GamepadAxis GamepadAxisValue => Kind == InputStateAxisSourceKind.GamepadAxis ? gamepadAxis :
		throw new InvalidOperationException("input state-axis source does not contain a gamepad axis");
	public DigitalAxisSource DigitalValue => Kind == InputStateAxisSourceKind.DigitalPair ? digital :
		throw new InvalidOperationException("input state-axis source does not contain a digital pair");

	private InputStateAxisSource(InputStateAxisSourceKind kind, GamepadAxis gamepadAxis, DigitalAxisSource digital) {
		Kind = kind;
		this.gamepadAxis = gamepadAxis;
		this.digital = digital;
	}

	public static InputStateAxisSource GamepadAxis(GamepadAxis axis) =>
		new InputStateAxisSource(InputStateAxisSourceKind.GamepadAxis, axis, default);
	public static InputStateAxisSource DigitalPair(InputButtonSource negative, InputButtonSource positive, SOCDPolicy socd = SOCDPolicy.Last) =>
		new InputStateAxisSource(InputStateAxisSourceKind.DigitalPair, default, new DigitalAxisSource(negative, positive, socd));

	public bool Equals(InputStateAxisSource other) => Kind == other.Kind && gamepadAxis == other.gamepadAxis && digital.Equals(other.digital);
	public override bool Equals(object? obj) => obj is InputStateAxisSource other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Kind, gamepadAxis, digital);
	public static bool operator ==(InputStateAxisSource left, InputStateAxisSource right) => left.Equals(right);
	public static bool operator !=(InputStateAxisSource left, InputStateAxisSource right) => !left.Equals(right);
}

public enum InputStateAxis2DSourceKind {
	GamepadStick,
	DigitalButtons,
	Pair
}

public enum GamepadStick {
	Left,
	Right
}

public readonly struct DigitalAxis2DSource : IEquatable<DigitalAxis2DSource> {
	public InputButtonSource Left { get; }
	public InputButtonSource Right { get; }
	public InputButtonSource Up { get; }
	public InputButtonSource Down { get; }
	public SOCDPolicy XSOCD { get; }
	public SOCDPolicy YSOCD { get; }

	public DigitalAxis2DSource(InputButtonSource left, InputButtonSource right, InputButtonSource up, InputButtonSource down,
		SOCDPolicy xSOCD = SOCDPolicy.Last, SOCDPolicy ySOCD = SOCDPolicy.Last) {
		if (left == right)
			throw new ArgumentException("left and right sources must be different");
		if (up == down)
			throw new ArgumentException("up and down sources must be different");
		Left = left;
		Right = right;
		Up = up;
		Down = down;
		XSOCD = xSOCD;
		YSOCD = ySOCD;
	}

	public bool Equals(DigitalAxis2DSource other) =>
		Left == other.Left && Right == other.Right && Up == other.Up && Down == other.Down &&
		XSOCD == other.XSOCD && YSOCD == other.YSOCD;
	public override bool Equals(object? obj) => obj is DigitalAxis2DSource other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Left, Right, Up, Down, XSOCD, YSOCD);
	public static bool operator ==(DigitalAxis2DSource left, DigitalAxis2DSource right) => left.Equals(right);
	public static bool operator !=(DigitalAxis2DSource left, DigitalAxis2DSource right) => !left.Equals(right);
}

public readonly struct StateAxis2DPairSource(InputStateAxisSource x, InputStateAxisSource y) : IEquatable<StateAxis2DPairSource> {
	public InputStateAxisSource X { get; } = x;
	public InputStateAxisSource Y { get; } = y;

	public bool Equals(StateAxis2DPairSource other) => X == other.X && Y == other.Y;
	public override bool Equals(object? obj) => obj is StateAxis2DPairSource other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(X, Y);
	public static bool operator ==(StateAxis2DPairSource left, StateAxis2DPairSource right) => left.Equals(right);
	public static bool operator !=(StateAxis2DPairSource left, StateAxis2DPairSource right) => !left.Equals(right);
}

public readonly struct InputStateAxis2DSource : IEquatable<InputStateAxis2DSource> {
	private readonly GamepadStick stick;
	private readonly DigitalAxis2DSource digital;
	private readonly StateAxis2DPairSource pair;

	public InputStateAxis2DSourceKind Kind { get; }
	public GamepadStick GamepadStickValue => Kind == InputStateAxis2DSourceKind.GamepadStick ? stick :
		throw new InvalidOperationException("2D state-axis source does not contain a gamepad stick");
	public DigitalAxis2DSource DigitalValue => Kind == InputStateAxis2DSourceKind.DigitalButtons ? digital :
		throw new InvalidOperationException("2D state-axis source does not contain digital buttons");
	public StateAxis2DPairSource PairValue => Kind == InputStateAxis2DSourceKind.Pair ? pair :
		throw new InvalidOperationException("2D state-axis source does not contain an axis pair");

	private InputStateAxis2DSource(InputStateAxis2DSourceKind kind, GamepadStick stick,
		DigitalAxis2DSource digital, StateAxis2DPairSource pair) {
		Kind = kind;
		this.stick = stick;
		this.digital = digital;
		this.pair = pair;
	}

	public static InputStateAxis2DSource GamepadStick(GamepadStick stick) =>
		new InputStateAxis2DSource(InputStateAxis2DSourceKind.GamepadStick, stick, default, default);
	public static InputStateAxis2DSource DigitalButtons(
		InputButtonSource left, InputButtonSource right, InputButtonSource up, InputButtonSource down,
		SOCDPolicy xSOCD = SOCDPolicy.Last, SOCDPolicy ySOCD = SOCDPolicy.Last
	) => new InputStateAxis2DSource(InputStateAxis2DSourceKind.DigitalButtons, default, new DigitalAxis2DSource(left, right, up, down, xSOCD, ySOCD), default);
	public static InputStateAxis2DSource Pair(InputStateAxisSource x, InputStateAxisSource y) =>
		new InputStateAxis2DSource(InputStateAxis2DSourceKind.Pair, default, default, new StateAxis2DPairSource(x, y));

	public bool Equals(InputStateAxis2DSource other) => Kind == other.Kind && stick == other.stick && digital.Equals(other.digital) && pair.Equals(other.pair);
	public override bool Equals(object? obj) => obj is InputStateAxis2DSource other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Kind, stick, digital, pair);
	public static bool operator ==(InputStateAxis2DSource left, InputStateAxis2DSource right) => left.Equals(right);
	public static bool operator !=(InputStateAxis2DSource left, InputStateAxis2DSource right) => !left.Equals(right);
}

public enum PointerWheelAxis {
	X,
	Y
}

public enum InputImpulseAxisSourceKind {
	PointerWheel
}

public readonly struct InputImpulseAxisSource : IEquatable<InputImpulseAxisSource> {
	private readonly PointerWheelAxis wheelAxis;

	public InputImpulseAxisSourceKind Kind { get; }
	public PointerWheelAxis PointerWheelAxisValue => Kind == InputImpulseAxisSourceKind.PointerWheel ? wheelAxis :
		throw new InvalidOperationException("input impulse-axis source does not contain a pointer wheel axis");

	private InputImpulseAxisSource(InputImpulseAxisSourceKind kind, PointerWheelAxis wheelAxis) {
		Kind = kind;
		this.wheelAxis = wheelAxis;
	}

	public static InputImpulseAxisSource PointerWheel(PointerWheelAxis axis) =>
		new InputImpulseAxisSource(InputImpulseAxisSourceKind.PointerWheel, axis);

	public bool Equals(InputImpulseAxisSource other) => Kind == other.Kind && wheelAxis == other.wheelAxis;
	public override bool Equals(object? obj) => obj is InputImpulseAxisSource other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Kind, wheelAxis);
	public static bool operator ==(InputImpulseAxisSource left, InputImpulseAxisSource right) => left.Equals(right);
	public static bool operator !=(InputImpulseAxisSource left, InputImpulseAxisSource right) => !left.Equals(right);
}
