// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Input;

[ClosedEnum]
public readonly partial struct Key {
	public enum Case {
		Unknown,

		A, B, C, D, E, F, G, H, I, J, K, L, M,
		N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

		Digit0, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9,

		F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

		Escape,
		Tab,
		CapsLock,
		Backspace,
		Enter,
		Space,

		Minus,
		Equal,
		LeftBracket,
		RightBracket,
		Backslash,
		Semicolon,
		Apostrophe,
		Grave,
		Comma,
		Period,
		Slash,

		PrintScreen,
		ScrollLock,
		Pause,

		Insert,
		Delete,
		Home,
		End,
		PageUp,
		PageDown,

		Left,
		Right,
		Up,
		Down,

		NumLock,
		NumpadDivide,
		NumpadMultiply,
		NumpadMinus,
		NumpadPlus,
		NumpadEnter,
		Numpad0,
		Numpad1,
		Numpad2,
		Numpad3,
		Numpad4,
		Numpad5,
		Numpad6,
		Numpad7,
		Numpad8,
		Numpad9,
		NumpadPeriod,

		LeftCtrl,
		RightCtrl,
		LeftShift,
		RightShift,
		LeftAlt,
		RightAlt,
		LeftGui,
		RightGui,

		Application
	}
}

[ClosedEnum]
public readonly partial struct GamepadAxis {
	public enum Case {
		Unknown,

		LeftX, LeftY,
		RightX, RightY,
		LeftTrigger, RightTrigger
	}
}

[ClosedEnum]
public readonly partial struct GamepadButton {
	public enum Case {
		Unknown,

		South, East, West, North,
		Back, Guide, Start,
		LeftStick, RightStick,
		LeftShoulder, RightShoulder,
		DpadUp, DpadDown, DpadLeft, DpadRight,

		// situational but why not
		Misc1,
		RightPaddle1, LeftPaddle1, RightPaddle2, LeftPaddle2,
		Touchpad
	}
}

[ClosedEnum]
public readonly partial struct PointerButton {
	public enum Case {
		Unknown, // this one's probably unnecessary but who knows

		Left,
		Right,
		Middle,
		X1,
		X2
	}
}
