// SPDX-License-Identifier: MIT

using System;

namespace Injure.Input;

public readonly struct InputSnapshot(KeyboardState keyboard, PointerState pointer, GamepadStateSet gamepads) {
	public static readonly InputSnapshot Rest = default;

	public KeyboardState Keyboard { get; } = keyboard;
	public PointerState Pointer { get; } = pointer;
	public GamepadStateSet Gamepads { get; } = gamepads;
}

public readonly ref struct InputView(ReadOnlySpan<InputEvent> events, InputSnapshot state) {
	public static InputView Empty => new(ReadOnlySpan<InputEvent>.Empty, InputSnapshot.Rest);
	public static InputView EmptyWith(InputSnapshot state) => new(ReadOnlySpan<InputEvent>.Empty, state);

	public ReadOnlySpan<InputEvent> Events { get; } = events;
	public InputSnapshot State { get; } = state;
}
