// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Hexa.NET.SDL3;

using Injure.Timing;

namespace Injure.Input;

public readonly struct InputCursor : IEquatable<InputCursor>, IComparable<InputCursor> {
	internal readonly ulong Seq;
	internal InputCursor(ulong seq) {
		Seq = seq;
	}

	public bool Equals(InputCursor other) => Seq == other.Seq;
	public override bool Equals(object? obj) => obj is InputCursor other && Equals(other);
	public override int GetHashCode() => Seq.GetHashCode();
	public int CompareTo(InputCursor other) => Seq.CompareTo(other.Seq);

	public static bool operator ==(InputCursor left, InputCursor right) => left.Equals(right);
	public static bool operator !=(InputCursor left, InputCursor right) => !left.Equals(right);
	public static bool operator <(InputCursor left, InputCursor right) => left.Seq < right.Seq;
	public static bool operator >(InputCursor left, InputCursor right) => left.Seq > right.Seq;
	public static bool operator <=(InputCursor left, InputCursor right) => left.Seq <= right.Seq;
	public static bool operator >=(InputCursor left, InputCursor right) => left.Seq >= right.Seq;
}

internal sealed class InputSystem {
	private struct MutableGamepadState {
		public GamepadID ID;
		public uint Buttons;
		public float LeftX;
		public float LeftY;
		public float RightX;
		public float RightY;
		public float LeftTrigger;
		public float RightTrigger;
	}

	private readonly List<InputEvent> events = new();
	private ulong headSeq = 0;
	private ulong nextSeq = 0;

	private ulong keys0;
	private ulong keys1;
	private ulong keys2;
	private ulong keys3;

	private byte pointerButtons;
	private float pointerX;
	private float pointerY;
	private bool pointerInsideWindow;
	private bool pointerCaptured;

	private readonly List<MutableGamepadState> gamepads = new();
	private readonly Dictionary<uint, int> gamepadIdxBySdlInstanceID = new();
	private uint nextGamepadID = 0; // first will be 1 since this gets incremented upfront

	public InputSnapshot CurrentState => new(
		new KeyboardState(keys0, keys1, keys2, keys3),
		new PointerState(pointerButtons, pointerX, pointerY, pointerInsideWindow, pointerCaptured),
		snapshotGamepads()
	);

	public InputCursor CreateCursor() => new(nextSeq);

	public InputView CreateViewSince(InputCursor cursor, out InputCursor next) => new(ReadSince(cursor, out next), CurrentState);
	public InputView CreateViewSince(ref InputCursor cursor) => new(ReadSince(cursor, out cursor), CurrentState);

	public ReadOnlySpan<InputEvent> ReadSince(InputCursor cursor, out InputCursor next) {
		if (cursor.Seq < headSeq || cursor.Seq > nextSeq)
			throw new ArgumentOutOfRangeException(nameof(cursor));
		int start = checked((int)(cursor.Seq - headSeq));
		next = new InputCursor(nextSeq);
		return CollectionsMarshal.AsSpan(events)[start..]; // TODO: Dont
	}

	public void AdvanceToCurrent(ref InputCursor cursor) {
		cursor = new InputCursor(nextSeq);
	}

	public void Push(InputEvent ev) {
		ArgumentNullException.ThrowIfNull(ev);
		applyEventToState(ev);
		events.Add(ev);
		nextSeq++;
	}

	public void SetPointerInsideWindow(bool inside) => pointerInsideWindow = inside;
	public void SetPointerCaptured(bool captured) => pointerCaptured = captured;

	public void DiscardBefore(InputCursor cursor) {
		if (cursor.Seq < headSeq || cursor.Seq > nextSeq)
			throw new ArgumentOutOfRangeException(nameof(cursor));
		int toRemove = checked((int)(cursor.Seq - headSeq));
		if (toRemove == 0)
			return;
		events.RemoveRange(0, toRemove);
		headSeq = cursor.Seq;
	}

	public void DiscardAll() {
		headSeq = nextSeq;
		events.Clear();
	}

	public void ClearKeyboardAndPointer(MonoTick tick) {
		synthesizeKeyboardReleaseAll(tick);
		synthesizePointerReleaseAll(tick);
		keys0 = keys1 = keys2 = keys3 = 0;
		pointerButtons = 0;
	}

	public void ClearAllDevices(MonoTick tick) {
		synthesizeKeyboardReleaseAll(tick);
		synthesizePointerReleaseAll(tick);
		synthesizeGamepadsReleaseAll(tick);
		keys0 = keys1 = keys2 = keys3 = 0;
		pointerButtons = 0;
		for (int i = 0; i < gamepads.Count; i++) {
			MutableGamepadState g = gamepads[i];
			g.Buttons = 0;
			g.LeftX = g.LeftY = g.RightX = g.RightY = g.LeftTrigger = g.RightTrigger = 0f;
			gamepads[i] = g;
		}
	}

	private void applyEventToState(InputEvent ev) {
		switch (ev) {
		case KeyEvent keyEv:
			setKey(keyEv.Key, keyEv.Edge == EdgeType.Press);
			break;
		case PointerMoveEvent pmoveEv:
			pointerX = pmoveEv.X;
			pointerY = pmoveEv.Y;
			break;
		case PointerButtonEvent pbtnEv:
			setPointerButton(pbtnEv.Button, pbtnEv.Edge == EdgeType.Press);
			pointerX = pbtnEv.X;
			pointerY = pbtnEv.Y;
			break;
		case GamepadAddedEvent gaddEv:
			if (!tryFindGamepad(gaddEv.Gamepad, out _))
				gamepads.Add(new MutableGamepadState { ID = gaddEv.Gamepad });
			break;
		case GamepadRemovedEvent gremEv:
			removeGamepad(gremEv.Gamepad);
			break;
		case GamepadAxisEvent gaxisEv:
			if (tryFindGamepad(gaxisEv.Gamepad, out int idxAxis)) {
				MutableGamepadState g = gamepads[idxAxis];
				setGamepadAxis(ref g, gaxisEv.Axis, gaxisEv.Value);
				gamepads[idxAxis] = g;
			}
			break;
		case GamepadButtonEvent gbtnEv:
			if (tryFindGamepad(gbtnEv.Gamepad, out int idxBtn)) {
				MutableGamepadState g = gamepads[idxBtn];
				setGamepadButton(ref g, gbtnEv.Button, gbtnEv.Edge == EdgeType.Press);
				gamepads[idxBtn] = g;
			}
			break;
		}
	}

	private bool tryFindGamepad(GamepadID id, out int idx) {
		for (int i = 0; i < gamepads.Count; i++) {
			if (gamepads[i].ID == id) {
				idx = i;
				return true;
			}
		}
		idx = -1;
		return false;
	}

	private void removeGamepad(GamepadID id) {
		for (int i = 0; i < gamepads.Count; i++) {
			if (gamepads[i].ID != id)
				continue;
			gamepads.RemoveAt(i);
			foreach (uint k in gamepadIdxBySdlInstanceID.Keys) {
				int mapped = gamepadIdxBySdlInstanceID[k];
				if (mapped == i)
					gamepadIdxBySdlInstanceID.Remove(k);
				else if (mapped > i)
					gamepadIdxBySdlInstanceID[k] = mapped - 1;
			}
			return;
		}
	}

	private GamepadStateSet snapshotGamepads() {
		if (gamepads.Count == 0)
			return GamepadStateSet.Rest;
		GamepadStateEntry[] arr = new GamepadStateEntry[gamepads.Count];
		for (int i = 0; i < gamepads.Count; i++) {
			MutableGamepadState g = gamepads[i];
			arr[i] = new GamepadStateEntry(
				g.ID,
				new GamepadState(g.Buttons, g.LeftX, g.LeftY, g.RightX, g.RightY, g.LeftTrigger, g.RightTrigger)
			);
		}
		return new GamepadStateSet(arr);
	}

	private void setKey(Key key, bool down) {
		int idx = (int)key.Tag;
		if ((uint)idx > 0xffu)
			return;
		int word = idx >> 6;
		ulong mask = 1ul << (idx & 0b111111);
		switch (word) {
		case 0: if (down) keys0 |= mask; else keys0 &= ~mask; break;
		case 1: if (down) keys1 |= mask; else keys1 &= ~mask; break;
		case 2: if (down) keys2 |= mask; else keys2 &= ~mask; break;
		case 3: if (down) keys3 |= mask; else keys3 &= ~mask; break;
		default: throw new UnreachableException();
		}
	}

	private void setPointerButton(PointerButton button, bool down) {
		int idx = (int)button.Tag;
		if ((uint)idx >= 8u)
			return;
		byte mask = (byte)(1u << idx);
		if (down) pointerButtons |= mask; else pointerButtons &= (byte)~mask;
	}

	private static void setGamepadAxis(ref MutableGamepadState g, GamepadAxis axis, float value) {
		switch (axis.Tag) {
		case GamepadAxis.Case.LeftX: g.LeftX = value; break;
		case GamepadAxis.Case.LeftY: g.LeftY = value; break;
		case GamepadAxis.Case.RightX: g.RightX = value; break;
		case GamepadAxis.Case.RightY: g.RightY = value; break;
		case GamepadAxis.Case.LeftTrigger: g.LeftTrigger = value; break;
		case GamepadAxis.Case.RightTrigger: g.RightTrigger = value; break;
		}
	}

	private static void setGamepadButton(ref MutableGamepadState g, GamepadButton button, bool down) {
		int idx = (int)button.Tag;
		if ((uint)idx >= 32u)
			return;
		uint mask = 1u << idx;
		if (down) g.Buttons |= mask; else g.Buttons &= ~mask;
	}

	private void synthesizeKeyboardReleaseAll(MonoTick tick) {
		for (int i = 0; i <= 0xff; i++) {
			Key key = Key.Enum.FromTag((Key.Case)i);
			if (!new KeyboardState(keys0, keys1, keys2, keys3).IsDown(key))
				continue;
			Push(new KeyEvent(tick, key, EdgeType.Release));
		}
	}

	private void synthesizePointerReleaseAll(MonoTick tick) {
		for (int i = 0; i < 8; i++) {
			PointerButton btn = PointerButton.Enum.FromTag((PointerButton.Case)i);
			if (!new PointerState(pointerButtons, pointerX, pointerY, pointerInsideWindow, pointerCaptured).IsDown(btn))
				continue;
			Push(new PointerButtonEvent(tick, btn, EdgeType.Release, Clicks: 0, pointerX, pointerY));
		}
	}

	private void synthesizeGamepadsReleaseAll(MonoTick tick) {
		foreach (MutableGamepadState g in gamepads) {
			for (int bit = 0; bit < 32; bit++) {
				if ((g.Buttons & (1u << bit)) == 0)
					continue;
				Push(new GamepadButtonEvent(tick, g.ID, GamepadButton.Enum.FromTag((GamepadButton.Case)bit), EdgeType.Release));
			}
			if (g.LeftX != 0f) Push(new GamepadAxisEvent(tick, g.ID, GamepadAxis.LeftX, 0f));
			if (g.LeftY != 0f) Push(new GamepadAxisEvent(tick, g.ID, GamepadAxis.LeftY, 0f));
			if (g.RightX != 0f) Push(new GamepadAxisEvent(tick, g.ID, GamepadAxis.RightX, 0f));
			if (g.RightY != 0f) Push(new GamepadAxisEvent(tick, g.ID, GamepadAxis.RightY, 0f));
			if (g.LeftTrigger != 0f) Push(new GamepadAxisEvent(tick, g.ID, GamepadAxis.LeftTrigger, 0f));
			if (g.RightTrigger != 0f) Push(new GamepadAxisEvent(tick, g.ID, GamepadAxis.RightTrigger, 0f));
		}
	}

	public bool TryHandleSDLEvent(in SDLEvent ev) {
		SDLEventType t = (SDLEventType)ev.Type;
		if (t is SDLEventType.KeyDown or SDLEventType.KeyUp) {
			if (ev.Key.Repeat != 0)
				return true;
			Key key = TranslateScancode(ev.Key.Scancode);
			if (key != Key.Unknown)
				Push(new KeyEvent((MonoTick)ev.Key.Timestamp, key, ev.Key.Down != 0 ? EdgeType.Press : EdgeType.Release));
			return true;
		} else if (t == SDLEventType.GamepadAdded) {
			uint inst = checked((uint)ev.Gdevice.Which);
			if (gamepadIdxBySdlInstanceID.ContainsKey(inst))
				return true;
			GamepadID id = new(++nextGamepadID);
			gamepadIdxBySdlInstanceID.Add(inst, gamepads.Count);
			Push(new GamepadAddedEvent((MonoTick)ev.Gdevice.Timestamp, id));
			return true;
		} else if (t == SDLEventType.GamepadRemoved) {
			uint inst = checked((uint)ev.Gdevice.Which);
			if (!gamepadIdxBySdlInstanceID.TryGetValue(inst, out int idx))
				return true;
			Push(new GamepadRemovedEvent((MonoTick)ev.Gdevice.Timestamp, gamepads[idx].ID));
			return true;
		} else if (t == SDLEventType.GamepadAxisMotion) {
			uint inst = checked((uint)ev.Gdevice.Which);
			if (!gamepadIdxBySdlInstanceID.TryGetValue(inst, out int idx))
				return true;
			GamepadAxis axis = TranslateGamepadAxis((SDLGamepadAxis)ev.Gaxis.Axis);
			if (axis != GamepadAxis.Unknown) {
				float v = NormalizeGamepadAxis(axis, ev.Gaxis.Value);
				Push(new GamepadAxisEvent((MonoTick)ev.Gaxis.Timestamp, gamepads[idx].ID, axis, v));
			}
			return true;
		} else if (t is SDLEventType.GamepadButtonDown or SDLEventType.GamepadButtonUp) {
			uint inst = checked((uint)ev.Gdevice.Which);
			if (!gamepadIdxBySdlInstanceID.TryGetValue(inst, out int idx))
				return true;
			GamepadButton btn = TranslateGamepadButton((SDLGamepadButton)ev.Gbutton.Button);
			if (btn != GamepadButton.Unknown)
				Push(new GamepadButtonEvent((MonoTick)ev.Gbutton.Timestamp, gamepads[idx].ID, btn,
					ev.Gbutton.Down != 0 ? EdgeType.Press : EdgeType.Release));
			return true;
		} else if (t == SDLEventType.MouseMotion) {
			Push(new PointerMoveEvent((MonoTick)ev.Motion.Timestamp, ev.Motion.X, ev.Motion.Y, ev.Motion.Xrel, ev.Motion.Yrel));
			return true;
		} else if (t is SDLEventType.MouseButtonDown or SDLEventType.MouseButtonUp) {
			PointerButton btn = TranslatePointerButton(ev.Button.Button);
			if (btn != PointerButton.Unknown)
				Push(new PointerButtonEvent((MonoTick)ev.Button.Timestamp, btn,
					ev.Button.Down != 0 ? EdgeType.Press : EdgeType.Release, ev.Button.Clicks, ev.Button.X, ev.Button.Y));
			return true;
		} else if (t == SDLEventType.MouseWheel) {
			float x = ev.Wheel.X;
			float y = ev.Wheel.Y;
			int ix = ev.Wheel.IntegerX;
			int iy = ev.Wheel.IntegerY;
			if (ev.Wheel.Direction == SDLMouseWheelDirection.Flipped) {
				x = -x;
				y = -y;
				ix = -ix;
				iy = -iy;
			}
			Push(new PointerWheelEvent((MonoTick)ev.Wheel.Timestamp, x, y, ix, iy, ev.Wheel.MouseX, ev.Wheel.MouseY));
			return true;
		} else if (t == SDLEventType.TextInput) {
			unsafe {
				string? s = Marshal.PtrToStringUTF8((IntPtr)ev.Text.Text);
				if (s is not null)
					Push(new TextEnteredEvent((MonoTick)ev.Text.Timestamp, s));
			}
			return true;
		}
		return false;
	}

	public static Key TranslateScancode(SDLScancode scancode) => scancode switch {
		SDLScancode.A => Key.A,
		SDLScancode.B => Key.B,
		SDLScancode.C => Key.C,
		SDLScancode.D => Key.D,
		SDLScancode.E => Key.E,
		SDLScancode.F => Key.F,
		SDLScancode.G => Key.G,
		SDLScancode.H => Key.H,
		SDLScancode.I => Key.I,
		SDLScancode.J => Key.J,
		SDLScancode.K => Key.K,
		SDLScancode.L => Key.L,
		SDLScancode.M => Key.M,
		SDLScancode.N => Key.N,
		SDLScancode.O => Key.O,
		SDLScancode.P => Key.P,
		SDLScancode.Q => Key.Q,
		SDLScancode.R => Key.R,
		SDLScancode.S => Key.S,
		SDLScancode.T => Key.T,
		SDLScancode.U => Key.U,
		SDLScancode.V => Key.V,
		SDLScancode.W => Key.W,
		SDLScancode.X => Key.X,
		SDLScancode.Y => Key.Y,
		SDLScancode.Z => Key.Z,

		SDLScancode.Scancode0 => Key.Digit0,
		SDLScancode.Scancode1 => Key.Digit1,
		SDLScancode.Scancode2 => Key.Digit2,
		SDLScancode.Scancode3 => Key.Digit3,
		SDLScancode.Scancode4 => Key.Digit4,
		SDLScancode.Scancode5 => Key.Digit5,
		SDLScancode.Scancode6 => Key.Digit6,
		SDLScancode.Scancode7 => Key.Digit7,
		SDLScancode.Scancode8 => Key.Digit8,
		SDLScancode.Scancode9 => Key.Digit9,

		SDLScancode.F1 => Key.F1,
		SDLScancode.F2 => Key.F2,
		SDLScancode.F3 => Key.F3,
		SDLScancode.F4 => Key.F4,
		SDLScancode.F5 => Key.F5,
		SDLScancode.F6 => Key.F6,
		SDLScancode.F7 => Key.F7,
		SDLScancode.F8 => Key.F8,
		SDLScancode.F9 => Key.F9,
		SDLScancode.F10 => Key.F10,
		SDLScancode.F11 => Key.F11,
		SDLScancode.F12 => Key.F12,

		SDLScancode.Escape => Key.Escape,
		SDLScancode.Tab => Key.Tab,
		SDLScancode.Capslock => Key.CapsLock,
		SDLScancode.Backspace => Key.Backspace,
		SDLScancode.Return => Key.Enter,
		SDLScancode.Space => Key.Space,

		SDLScancode.Minus => Key.Minus,
		SDLScancode.Equals => Key.Equal,
		SDLScancode.Leftbracket => Key.LeftBracket,
		SDLScancode.Rightbracket => Key.RightBracket,
		SDLScancode.Backslash => Key.Backslash,
		SDLScancode.Semicolon => Key.Semicolon,
		SDLScancode.Apostrophe => Key.Apostrophe,
		SDLScancode.Grave => Key.Grave,
		SDLScancode.Comma => Key.Comma,
		SDLScancode.Period => Key.Period,
		SDLScancode.Slash => Key.Slash,

		SDLScancode.Printscreen => Key.PrintScreen,
		SDLScancode.Scrolllock => Key.ScrollLock,
		SDLScancode.Pause => Key.Pause,

		SDLScancode.Insert => Key.Insert,
		SDLScancode.Delete => Key.Delete,
		SDLScancode.Home => Key.Home,
		SDLScancode.End => Key.End,
		SDLScancode.Pageup => Key.PageUp,
		SDLScancode.Pagedown => Key.PageDown,

		SDLScancode.Left => Key.Left,
		SDLScancode.Right => Key.Right,
		SDLScancode.Up => Key.Up,
		SDLScancode.Down => Key.Down,

		SDLScancode.Numlockclear => Key.NumLock,
		SDLScancode.KpDivide => Key.NumpadDivide,
		SDLScancode.KpMultiply => Key.NumpadMultiply,
		SDLScancode.KpMinus => Key.NumpadMinus,
		SDLScancode.KpPlus => Key.NumpadPlus,
		SDLScancode.KpEnter => Key.NumpadEnter,
		SDLScancode.Kp0 => Key.Numpad0,
		SDLScancode.Kp1 => Key.Numpad1,
		SDLScancode.Kp2 => Key.Numpad2,
		SDLScancode.Kp3 => Key.Numpad3,
		SDLScancode.Kp4 => Key.Numpad4,
		SDLScancode.Kp5 => Key.Numpad5,
		SDLScancode.Kp6 => Key.Numpad6,
		SDLScancode.Kp7 => Key.Numpad7,
		SDLScancode.Kp8 => Key.Numpad8,
		SDLScancode.Kp9 => Key.Numpad9,
		SDLScancode.KpPeriod => Key.NumpadPeriod,

		SDLScancode.Lctrl => Key.LeftCtrl,
		SDLScancode.Rctrl => Key.RightCtrl,
		SDLScancode.Lshift => Key.LeftShift,
		SDLScancode.Rshift => Key.RightShift,
		SDLScancode.Lalt => Key.LeftAlt,
		SDLScancode.Ralt => Key.RightAlt,
		SDLScancode.Lgui => Key.LeftGui,
		SDLScancode.Rgui => Key.RightGui,

		SDLScancode.Application => Key.Application,

		_ => Key.Unknown
	};

	public static GamepadAxis TranslateGamepadAxis(SDLGamepadAxis axis) => axis switch {
		SDLGamepadAxis.Leftx => GamepadAxis.LeftX,
		SDLGamepadAxis.Lefty => GamepadAxis.LeftY,
		SDLGamepadAxis.Rightx => GamepadAxis.RightX,
		SDLGamepadAxis.Righty => GamepadAxis.RightY,
		SDLGamepadAxis.LeftTrigger => GamepadAxis.LeftTrigger,
		SDLGamepadAxis.RightTrigger => GamepadAxis.RightTrigger,
		_ => GamepadAxis.Unknown
	};

	public static GamepadButton TranslateGamepadButton(SDLGamepadButton button) => button switch {
		SDLGamepadButton.South => GamepadButton.South,
		SDLGamepadButton.East => GamepadButton.East,
		SDLGamepadButton.West => GamepadButton.West,
		SDLGamepadButton.North => GamepadButton.North,
		SDLGamepadButton.Back => GamepadButton.Back,
		SDLGamepadButton.Guide => GamepadButton.Guide,
		SDLGamepadButton.Start => GamepadButton.Start,
		SDLGamepadButton.LeftStick => GamepadButton.LeftStick,
		SDLGamepadButton.RightStick => GamepadButton.RightStick,
		SDLGamepadButton.LeftShoulder => GamepadButton.LeftShoulder,
		SDLGamepadButton.RightShoulder => GamepadButton.RightShoulder,
		SDLGamepadButton.DpadUp => GamepadButton.DpadUp,
		SDLGamepadButton.DpadDown => GamepadButton.DpadDown,
		SDLGamepadButton.DpadLeft => GamepadButton.DpadLeft,
		SDLGamepadButton.DpadRight => GamepadButton.DpadRight,
		SDLGamepadButton.Misc1 => GamepadButton.Misc1,
		SDLGamepadButton.RightPaddle1 => GamepadButton.RightPaddle1,
		SDLGamepadButton.RightPaddle2 => GamepadButton.RightPaddle2,
		SDLGamepadButton.LeftPaddle1 => GamepadButton.LeftPaddle1,
		SDLGamepadButton.LeftPaddle2 => GamepadButton.LeftPaddle2,
		SDLGamepadButton.Touchpad => GamepadButton.Touchpad,
		_ => GamepadButton.Unknown
	};

	public static PointerButton TranslatePointerButton(byte button) => button switch {
		SDL.SDL_BUTTON_LEFT => PointerButton.Left,
		SDL.SDL_BUTTON_RIGHT => PointerButton.Right,
		SDL.SDL_BUTTON_MIDDLE => PointerButton.Middle,
		SDL.SDL_BUTTON_X1 => PointerButton.X1,
		SDL.SDL_BUTTON_X2 => PointerButton.X2,
		_ => PointerButton.Unknown
	};

	public static float NormalizeGamepadAxis(GamepadAxis axis, short raw) {
		static float normalizeStick(short v) => v < 0 ? (float)v / 32768f : (float)v / 32767f;
		static float normalizeTrigger(short v) => Math.Clamp((float)v / 32767f, 0f, 1f);
		return axis.Tag switch {
			GamepadAxis.Case.LeftX or GamepadAxis.Case.LeftY or GamepadAxis.Case.RightX or GamepadAxis.Case.RightY => normalizeStick(raw),
			GamepadAxis.Case.LeftTrigger or GamepadAxis.Case.RightTrigger => normalizeTrigger(raw),
			_ => throw new ArgumentOutOfRangeException(nameof(axis), $"unknown gamepad axis '{axis}'")
		};
	}
}
