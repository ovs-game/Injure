// SPDX-License-Identifier: MIT

using System;

using Injure.Input;

namespace TestGame;

public static class Actions {
	public static ActionID Move { get; private set; }
	public static ActionID Confirm { get; private set; }
	public static ActionID Pause { get; private set; }
	public static ActionID ScrollY { get; private set; }

	public static ActionProfile Profile { get => field ?? throw new InvalidOperationException("Actions.Init() not called yet"); private set; }

	public static void Init() {
		TestGame.Input.Actions.RegisterMany(TestGame.OwnerID, reg => {
			Move = reg.Register("move");
			Confirm = reg.Register("confirm");
			Pause = reg.Register("pause");
			ScrollY = reg.Register("scrollY");
		});

		ActionMapBuilder b = new();

		b.BindStateAxis2D(Move, InputStateAxis2DSource.DigitalButtons(
			InputButtonSource.Key(Key.A),
			InputButtonSource.Key(Key.D),
			InputButtonSource.Key(Key.W),
			InputButtonSource.Key(Key.S)
		), Axis2DDeadzone.None);
		b.BindStateAxis2D(Move, InputStateAxis2DSource.GamepadStick(
			GamepadStick.Left
		), Axis2DDeadzone.ScaledRadial(0.15f));

		b.BindButton(Confirm, InputButtonSource.Key(Key.Space));
		b.BindButton(Confirm, InputButtonSource.GamepadButton(GamepadButton.South));
		b.BindButton(Confirm, InputButtonSource.PointerButton(PointerButton.Left));

		b.BindButton(Pause, InputButtonSource.Key(Key.Escape));
		b.BindButton(Pause, InputButtonSource.GamepadButton(GamepadButton.Start));

		b.BindImpulseAxis(ScrollY, InputImpulseAxisSource.PointerWheel(PointerWheelAxis.Y));

		Profile = new ActionProfile(b.ToSnapshot());
	}
}
