// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Injure.Input;

public readonly struct KeyboardState {
	private readonly ulong keys0;
	private readonly ulong keys1;
	private readonly ulong keys2;
	private readonly ulong keys3;

	public static readonly KeyboardState Rest = default;

	public KeyboardState(ReadOnlySpan<Key> down) {
		foreach (Key key in down) {
			int idx = (int)key.Tag;
			if ((uint)idx > 0xffu)
				throw new ArgumentOutOfRangeException(nameof(down), $"key '{key}' doesn't fit into the 256-bit bitset");
			int word = idx >> 6;
			int bit = idx & 0b111111;
			switch (word) {
			case 0: keys0 |= 1ul << bit; break;
			case 1: keys1 |= 1ul << bit; break;
			case 2: keys2 |= 1ul << bit; break;
			case 3: keys3 |= 1ul << bit; break;
			default: throw new UnreachableException(); // if 0 <= x <= 255 then x >> 6 can't be above 3
			}
		}
	}

	public KeyboardState(params Key[] down) : this(down.AsSpan()) {
	}

	internal KeyboardState(ulong bitset0, ulong bitset1, ulong bitset2, ulong bitset3) {
		keys0 = bitset0;
		keys1 = bitset1;
		keys2 = bitset2;
		keys3 = bitset3;
	}

	public bool IsDown(Key key) {
		int idx = (int)key.Tag;
		if ((uint)idx > 0xffu)
			return false;
		int word = idx >> 6;
		int bit = idx & 0b111111;
		return word switch {
			0 => (keys0 & (1ul << bit)) != 0,
			1 => (keys1 & (1ul << bit)) != 0,
			2 => (keys2 & (1ul << bit)) != 0,
			3 => (keys3 & (1ul << bit)) != 0,
			_ => throw new UnreachableException(), // if 0 <= x <= 255 then x >> 6 can't be above 3
		};
	}

	public bool Ctrl => IsDown(Key.LeftCtrl) || IsDown(Key.RightCtrl);
	public bool Shift => IsDown(Key.LeftShift) || IsDown(Key.RightShift);
	public bool Alt => IsDown(Key.LeftAlt) || IsDown(Key.RightAlt);
	public bool Gui => IsDown(Key.LeftGui) || IsDown(Key.RightGui);
}

public readonly struct GamepadState {
	private readonly uint buttons;

	public float LeftX { get; }
	public float LeftY { get; }
	public float RightX { get; }
	public float RightY { get; }
	public float LeftTrigger { get; }
	public float RightTrigger { get; }

	public static readonly GamepadState Rest = default;

	public GamepadState(ReadOnlySpan<GamepadButton> down, float leftX, float leftY, float rightX, float rightY,
		float leftTrigger, float rightTrigger) {
		static void checkStick(float v, string paramName) {
			if (v < -1f || v > 1f)
				throw new ArgumentOutOfRangeException(paramName, "stick axis values must be within [-1, +1]");
		}
		static void checkTrigger(float v, string paramName) {
			if (v < 0f || v > 1f)
				throw new ArgumentOutOfRangeException(paramName, "trigger axis values must be within [0, +1]");
		}

		checkStick(leftX, nameof(leftX));   checkStick(leftY, nameof(leftY));
		checkStick(rightX, nameof(rightX)); checkStick(rightY, nameof(rightY));
		checkTrigger(leftTrigger, nameof(leftTrigger));
		checkTrigger(rightTrigger, nameof(rightTrigger));

		foreach (GamepadButton btn in down) {
			int idx = (int)btn.Tag;
			if ((uint)idx >= 32u)
				throw new ArgumentOutOfRangeException(nameof(down), $"gamepad button '{btn}' doesn't fit into the 32-bit bitset");
			buttons |= 1u << idx;
		}
		LeftX = leftX;
		LeftY = leftY;
		RightX = rightX;
		RightY = rightY;
		LeftTrigger = leftTrigger;
		RightTrigger = rightTrigger;
	}

	internal GamepadState(uint bitset, float leftX, float leftY, float rightX, float rightY, float leftTrigger, float rightTrigger) {
		buttons = bitset;
		LeftX = leftX;
		LeftY = leftY;
		RightX = rightX;
		RightY = rightY;
		LeftTrigger = leftTrigger;
		RightTrigger = rightTrigger;
	}

	public bool IsDown(GamepadButton button) {
		int idx = (int)button.Tag;
		if ((uint)idx >= 32u)
			return false;
		return (buttons & (1u << idx)) != 0;
	}

	public float GetAxis(GamepadAxis axis) {
		return axis.Tag switch {
			GamepadAxis.Case.LeftX => LeftX,
			GamepadAxis.Case.LeftY => LeftY,
			GamepadAxis.Case.RightX => RightX,
			GamepadAxis.Case.RightY => RightY,
			GamepadAxis.Case.LeftTrigger => LeftTrigger,
			GamepadAxis.Case.RightTrigger => RightTrigger,
			_ => 0f,
		};
	}
}

public readonly record struct GamepadStateEntry(
	GamepadID ID,
	GamepadState State
);

public readonly struct GamepadStateSet : IReadOnlyList<GamepadStateEntry> {
	private readonly GamepadStateEntry[]? entriesBacking;
	private ReadOnlySpan<GamepadStateEntry> entries => entriesBacking ?? ReadOnlySpan<GamepadStateEntry>.Empty;

	public static readonly GamepadStateSet Rest = default;
	public int Count => entries.Length;
	public GamepadStateEntry this[int idx] => entries[idx];

	public GamepadStateSet(ReadOnlySpan<GamepadStateEntry> gamepads) {
		entriesBacking = gamepads.ToArray();
		if (entriesBacking.Length != entriesBacking.DistinctBy(static e => e.ID).Count())
			throw new ArgumentException("entry list must not have duplicate gamepad IDs", nameof(gamepads));
	}

	public GamepadStateSet(params GamepadStateEntry[] gamepads) : this(gamepads.AsSpan()) {
	}

	public bool Contains(GamepadID gamepadID) => TryGetState(gamepadID, out _);

	public bool TryGetState(GamepadID gamepadID, out GamepadState state) {
		foreach (GamepadStateEntry ent in entries) {
			if (ent.ID == gamepadID) {
				state = ent.State;
				return true;
			}
		}
		state = GamepadState.Rest;
		return false;
	}

	public GamepadState GetStateOrRest(GamepadID gamepadID) => TryGetState(gamepadID, out GamepadState state) ? state : GamepadState.Rest;

	public ReadOnlySpan<GamepadStateEntry>.Enumerator GetEnumerator() => entries.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => (entriesBacking ?? []).GetEnumerator();
	IEnumerator<GamepadStateEntry> IEnumerable<GamepadStateEntry>.GetEnumerator() => ((IEnumerable<GamepadStateEntry>)(entriesBacking ?? [])).GetEnumerator();

	public ReadOnlySpan<GamepadStateEntry> AsSpan() => entries;
	public static implicit operator ReadOnlySpan<GamepadStateEntry>(GamepadStateSet s) => s.AsSpan();
}

public readonly struct PointerState {
	private readonly byte buttons;

	public float X { get; }
	public float Y { get; }
	public bool InsideWindow { get; }
	public bool Captured { get; } // if true, coordinates may be out of window bounds

	public static readonly PointerState Rest = default;

	public PointerState(ReadOnlySpan<PointerButton> down, float x, float y, bool insideWindow, bool captured) {
		foreach (PointerButton btn in down) {
			int idx = (int)btn.Tag;
			if ((uint)idx >= 8u)
				throw new ArgumentOutOfRangeException(nameof(down), $"pointer button '{btn}' doesn't fit into the 8-bit bitset");
			buttons |= (byte)(1u << idx);
		}
		X = x;
		Y = y;
		InsideWindow = insideWindow;
		Captured = captured;
	}

	internal PointerState(byte bitset, float x, float y, bool insideWindow, bool captured) {
		buttons = bitset;
		X = x;
		Y = y;
		InsideWindow = insideWindow;
		Captured = captured;
	}

	public bool IsDown(PointerButton button) {
		int idx = (int)button.Tag;
		if ((uint)idx >= 8u)
			return false;
		return (buttons & (1u << idx)) != 0;
	}
}
