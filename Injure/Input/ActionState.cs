// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace Injure.Input;

public readonly record struct ButtonActionState(bool Down, bool PreviousDown) {
	public bool Pressed => Down && !PreviousDown;
	public bool Released => !Down && PreviousDown;
}
public readonly record struct StateAxisActionState(float Value, float PreviousValue) {
	public float StepDelta => Value - PreviousValue;
}
public readonly record struct StateAxis2DActionState(Vector2 Value, Vector2 PreviousValue) {
	public Vector2 StepDelta => Value - PreviousValue;
}
public readonly record struct ImpulseAxisActionState(float Amount);

public readonly record struct ButtonActionStateEntry(ActionID Action, ButtonActionState State);
public readonly record struct StateAxisActionStateEntry(ActionID Action, StateAxisActionState State);
public readonly record struct StateAxis2DActionStateEntry(ActionID Action, StateAxis2DActionState State);
public readonly record struct ImpulseAxisActionStateEntry(ActionID Action, ImpulseAxisActionState State);

public sealed class ActionStateSnapshot {
	public ImmutableDictionary<ActionID, ButtonActionState> Buttons { get; }
	public ImmutableDictionary<ActionID, StateAxisActionState> StateAxes { get; }
	public ImmutableDictionary<ActionID, StateAxis2DActionState> StateAxes2D { get; }
	public ImmutableDictionary<ActionID, ImpulseAxisActionState> ImpulseAxes { get; }

	public static readonly ActionStateSnapshot Empty = new(
		ImmutableDictionary<ActionID, ButtonActionState>.Empty,
		ImmutableDictionary<ActionID, StateAxisActionState>.Empty,
		ImmutableDictionary<ActionID, StateAxis2DActionState>.Empty,
		ImmutableDictionary<ActionID, ImpulseAxisActionState>.Empty
	);

	public ActionStateSnapshot(
		ReadOnlySpan<ButtonActionStateEntry> buttons,
		ReadOnlySpan<StateAxisActionStateEntry> stateAxes,
		ReadOnlySpan<StateAxis2DActionStateEntry> stateAxes2D,
		ReadOnlySpan<ImpulseAxisActionStateEntry> impulseAxes
	) {
		static ImmutableDictionary<ActionID, TValue> copy<TEntry, TValue>(ReadOnlySpan<TEntry> entries, Func<TEntry, ActionID> getAction,
			Func<TEntry, TValue> getValue, string paramName) {
			Dictionary<ActionID, TValue> ret = new(entries.Length);
			foreach (TEntry entry in entries) {
				ActionID action = getAction(entry);
				if (!ret.TryAdd(action, getValue(entry)))
					throw new ArgumentException("action state entries must not contain duplicate actions", paramName);
			}
			return ret.ToImmutableDictionary();
		}

		Buttons = copy(buttons, static e => e.Action, static e => e.State, nameof(buttons));
		StateAxes = copy(stateAxes, static e => e.Action, static e => e.State, nameof(stateAxes));
		StateAxes2D = copy(stateAxes2D, static e => e.Action, static e => e.State, nameof(stateAxes2D));
		ImpulseAxes = copy(impulseAxes, static e => e.Action, static e => e.State, nameof(impulseAxes));
	}

	public ActionStateSnapshot(
		ImmutableDictionary<ActionID, ButtonActionState> buttons,
		ImmutableDictionary<ActionID, StateAxisActionState> stateAxes,
		ImmutableDictionary<ActionID, StateAxis2DActionState> stateAxes2D,
		ImmutableDictionary<ActionID, ImpulseAxisActionState> impulseAxes
	) {
		Buttons = buttons ?? throw new ArgumentNullException(nameof(buttons));
		StateAxes = stateAxes ?? throw new ArgumentNullException(nameof(stateAxes));
		StateAxes2D = stateAxes2D ?? throw new ArgumentNullException(nameof(stateAxes2D));
		ImpulseAxes = impulseAxes ?? throw new ArgumentNullException(nameof(impulseAxes));
	}

	public ButtonActionState GetButton(ActionID action) => Buttons.TryGetValue(action, out ButtonActionState state) ? state : default;
	public StateAxisActionState GetStateAxis(ActionID action) => StateAxes.TryGetValue(action, out StateAxisActionState state) ? state : default;
	public StateAxis2DActionState GetStateAxis2D(ActionID action) => StateAxes2D.TryGetValue(action, out StateAxis2DActionState state) ? state : default;
	public ImpulseAxisActionState GetImpulseAxis(ActionID action) => ImpulseAxes.TryGetValue(action, out ImpulseAxisActionState state) ? state : default;

	public ActionStateView AsView() => new(
		new ButtonActionStateView(Buttons),
		new StateAxisActionStateView(StateAxes),
		new StateAxis2DActionStateView(StateAxes2D),
		new ImpulseAxisActionStateView(ImpulseAxes)
	);
}

public readonly ref struct ButtonActionStateView {
	internal readonly IReadOnlyDictionary<ActionID, ButtonActionState> States;
	internal ButtonActionStateView(IReadOnlyDictionary<ActionID, ButtonActionState> states) {
		States = states;
	}
	public ButtonActionState this[ActionID action] => States.TryGetValue(action, out ButtonActionState state) ? state : default;
}
public readonly ref struct StateAxisActionStateView {
	internal readonly IReadOnlyDictionary<ActionID, StateAxisActionState> States;
	internal StateAxisActionStateView(IReadOnlyDictionary<ActionID, StateAxisActionState> states) {
		States = states;
	}
	public StateAxisActionState this[ActionID action] => States.TryGetValue(action, out StateAxisActionState state) ? state : default;
}
public readonly ref struct StateAxis2DActionStateView {
	internal readonly IReadOnlyDictionary<ActionID, StateAxis2DActionState> States;
	internal StateAxis2DActionStateView(IReadOnlyDictionary<ActionID, StateAxis2DActionState> states) {
		States = states;
	}
	public StateAxis2DActionState this[ActionID action] => States.TryGetValue(action, out StateAxis2DActionState state) ? state : default;
}
public readonly ref struct ImpulseAxisActionStateView {
	internal readonly IReadOnlyDictionary<ActionID, ImpulseAxisActionState> States;
	internal ImpulseAxisActionStateView(IReadOnlyDictionary<ActionID, ImpulseAxisActionState> states) {
		States = states;
	}
	public ImpulseAxisActionState this[ActionID action] => States.TryGetValue(action, out ImpulseAxisActionState state) ? state : default;
}

public readonly ref struct ActionStateView {
	public ButtonActionStateView Buttons { get; }
	public StateAxisActionStateView StateAxes { get; }
	public StateAxis2DActionStateView StateAxes2D { get; }
	public ImpulseAxisActionStateView ImpulseAxes { get; }

	public static ActionStateView Empty => ActionStateSnapshot.Empty.AsView();

	internal ActionStateView(ButtonActionStateView buttons, StateAxisActionStateView stateAxes, StateAxis2DActionStateView stateAxes2D, ImpulseAxisActionStateView impulseAxes) {
		Buttons = buttons;
		StateAxes = stateAxes;
		StateAxes2D = stateAxes2D;
		ImpulseAxes = impulseAxes;
	}

	public ActionStateSnapshot ToSnapshot() => new(
		Buttons.States.ToImmutableDictionary(),
		StateAxes.States.ToImmutableDictionary(),
		StateAxes2D.States.ToImmutableDictionary(),
		ImpulseAxes.States.ToImmutableDictionary()
	);
}
