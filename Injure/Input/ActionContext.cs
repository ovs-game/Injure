// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

using Injure.Timing;

namespace Injure.Input;

public sealed class ActionContext(ActionProfile profile) {
	private sealed class Lookup {
		public readonly Dictionary<InputButtonSource, List<ActionID>> ButtonActionsBySource = new();
		public readonly Dictionary<InputImpulseAxisSource, List<ImpulseAxisBinding>> ImpulseAxesBySource = new();
		public readonly HashSet<InputButtonSource> TrackedButtonSources = new();
		public readonly HashSet<ActionID> ButtonActions = new();
		public readonly HashSet<ActionID> StateAxisActions = new();
		public readonly HashSet<ActionID> StateAxis2DActions = new();
		public readonly HashSet<ActionID> ImpulseAxisActions = new();
	}

	private readonly ActionProfile profile = profile ?? throw new ArgumentNullException(nameof(profile));

	private ActionMapSnapshot map = profile.Current;
	private ulong mapVersion = 0;
	private Lookup lookup = new();

	private ulong nextPressStamp = 1;

	private readonly Dictionary<InputButtonSource, bool> buttonSourceDown = new();
	private readonly Dictionary<InputButtonSource, ulong> buttonSourcePressedAt = new();
	private readonly Dictionary<ActionID, int> buttonHeldCounts = new();
	private readonly Dictionary<ActionID, bool> previousButtonDown = new();

	private readonly Dictionary<ActionID, float> stateAxisValues = new();
	private readonly Dictionary<ActionID, Vector2> stateAxis2DValues = new();

	private readonly Dictionary<ActionID, ButtonActionState> buttonStates = new();
	private readonly Dictionary<ActionID, StateAxisActionState> stateAxisStates = new();
	private readonly Dictionary<ActionID, StateAxis2DActionState> stateAxis2DStates = new();
	private readonly Dictionary<ActionID, ImpulseAxisActionState> impulseAxisStates = new();

	private readonly Dictionary<ActionID, float> stepImpulseAmounts = new();

	private readonly List<ControlEvent> events = new();

	private bool forceEmitStateAxes;
	private bool forceEmitStateAxes2D;

	public ControlView Update(MonoTick tick, in InputView input) {
		events.Clear();
		buttonStates.Clear();
		stateAxisStates.Clear();
		stateAxis2DStates.Clear();
		impulseAxisStates.Clear();
		stepImpulseAmounts.Clear();

		refreshMapIfNeeded(tick, input.State);

		foreach (InputEvent ev in input.Events)
			processInputEvent(ev);

		evaluateButtonsFinal();
		evaluateStateAxesFinal(tick, input.State);
		evaluateStateAxes2DFinal(tick, input.State);
		evaluateImpulseAxesFinal();

		forceEmitStateAxes = false;
		forceEmitStateAxes2D = false;

		ActionStateView actions = new(
			new ButtonActionStateView(buttonStates),
			new StateAxisActionStateView(stateAxisStates),
			new StateAxis2DActionStateView(stateAxis2DStates),
			new ImpulseAxisActionStateView(impulseAxisStates)
		);
		return new ControlView(actions, CollectionsMarshal.AsSpan(events), input.State.Pointer);
	}

	private void refreshMapIfNeeded(MonoTick tick, InputSnapshot raw) {
		ulong version = profile.Version;
		if (version == mapVersion)
			return;

		synthesizeResetEvents(tick);

		map = profile.Current;
		mapVersion = version;
		lookup = makeLookup(map);

		buttonSourceDown.Clear();
		buttonSourcePressedAt.Clear();
		buttonHeldCounts.Clear();

		baselineButtonsFromCurrentState(tick, raw);

		forceEmitStateAxes = true;
		forceEmitStateAxes2D = true;
	}

	private void synthesizeResetEvents(MonoTick tick) {
		foreach ((ActionID action, bool down) in previousButtonDown)
			if (down)
				events.Add(new ButtonActionEvent(tick, action, EdgeType.Release));
		foreach ((ActionID action, float val) in stateAxisValues)
			if (val != 0f)
				events.Add(new StateAxisActionEvent(tick, action, 0f));
		foreach ((ActionID action, Vector2 val) in stateAxis2DValues)
			if (val != Vector2.Zero)
				events.Add(new StateAxis2DActionEvent(tick, action, Vector2.Zero));
	}

	private void baselineButtonsFromCurrentState(MonoTick tick, InputSnapshot raw) {
		foreach (InputButtonSource source in lookup.TrackedButtonSources) {
			if (!isButtonSourceDown(raw, source))
				continue;

			buttonSourceDown[source] = true;
			buttonSourcePressedAt[source] = nextPressStamp++;

			if (!lookup.ButtonActionsBySource.TryGetValue(source, out List<ActionID>? actions))
				continue;

			foreach (ActionID action in actions) {
				int held = buttonHeldCounts.TryGetValue(action, out int h) ? h : 0;
				buttonHeldCounts[action] = held + 1;
				events.Add(new ButtonActionEvent(tick, action, EdgeType.Press));
			}
		}
	}

	private static Lookup makeLookup(ActionMapSnapshot map) {
		Lookup ret = new();

		foreach (ButtonBinding b in map.ButtonBindings) {
			if (!ret.ButtonActionsBySource.TryGetValue(b.Source, out List<ActionID>? list)) {
				list = new();
				ret.ButtonActionsBySource.Add(b.Source, list);
			}
			list.Add(b.Action);
			ret.TrackedButtonSources.Add(b.Source);
			ret.ButtonActions.Add(b.Action);
		}

		foreach (StateAxisBinding b in map.StateAxisBindings) {
			ret.StateAxisActions.Add(b.Action);
			addTrackedSources(ret, b.Source);
		}

		foreach (StateAxis2DBinding b in map.StateAxis2DBindings) {
			ret.StateAxis2DActions.Add(b.Action);
			addTrackedSources(ret, b.Source);
		}

		foreach (ImpulseAxisBinding b in map.ImpulseAxisBindings) {
			if (!ret.ImpulseAxesBySource.TryGetValue(b.Source, out List<ImpulseAxisBinding>? list)) {
				list = new();
				ret.ImpulseAxesBySource.Add(b.Source, list);
			}
			list.Add(b);
			ret.ImpulseAxisActions.Add(b.Action);
		}

		return ret;
	}

	private static void addTrackedSources(Lookup lookup, InputStateAxisSource source) {
		if (source.Kind == InputStateAxisSourceKind.DigitalPair) {
			DigitalAxisSource d = source.DigitalValue;
			lookup.TrackedButtonSources.Add(d.Negative);
			lookup.TrackedButtonSources.Add(d.Positive);
		}
	}

	private static void addTrackedSources(Lookup lookup, InputStateAxis2DSource source) {
		switch (source.Kind.Tag) {
		case InputStateAxis2DSourceKind.Case.DigitalButtons:
			DigitalAxis2DSource d = source.DigitalValue;
			lookup.TrackedButtonSources.Add(d.Left);
			lookup.TrackedButtonSources.Add(d.Right);
			lookup.TrackedButtonSources.Add(d.Up);
			lookup.TrackedButtonSources.Add(d.Down);
			break;
		case InputStateAxis2DSourceKind.Case.Pair:
			StateAxis2DPairSource p = source.PairValue;
			addTrackedSources(lookup, p.X);
			addTrackedSources(lookup, p.Y);
			break;
		}
	}

	private void processInputEvent(InputEvent ev) {
		switch (ev) {
		case KeyEvent key:
			handleButtonSourceEdge(key.Tick, InputButtonSource.Key(key.Key), key.Edge, ButtonActionEventInfo.None);
			break;
		case GamepadButtonEvent gp:
			handleButtonSourceEdge(gp.Tick, InputButtonSource.GamepadButton(gp.Button), gp.Edge, ButtonActionEventInfo.None);
			break;
		case PointerMoveEvent move:
			events.Add(new PointerMoveControlEvent(move.Tick, move.X, move.Y, move.DeltaX, move.DeltaY));
			break;
		case PointerButtonEvent ptr:
			handleButtonSourceEdge(ptr.Tick, InputButtonSource.PointerButton(ptr.Button), ptr.Edge, ButtonActionEventInfo.FromPointer(ptr.X, ptr.Y, ptr.Clicks));
			break;
		case PointerWheelEvent wheel:
			handlePointerWheel(wheel);
			break;
		case TextEnteredEvent text:
			events.Add(new TextEnteredControlEvent(text.Tick, text.Text));
			break;
		}
	}

	private void handleButtonSourceEdge(MonoTick tick, InputButtonSource source, EdgeType edge, ButtonActionEventInfo info) {
		if (!lookup.TrackedButtonSources.Contains(source))
			return;

		bool wasDown = buttonSourceDown.TryGetValue(source, out bool old) && old;
		bool nowDown = edge == EdgeType.Press;
		if (wasDown == nowDown)
			return;

		buttonSourceDown[source] = nowDown;
		if (nowDown)
			buttonSourcePressedAt[source] = nextPressStamp++;
		else
			buttonSourcePressedAt.Remove(source);

		if (!lookup.ButtonActionsBySource.TryGetValue(source, out List<ActionID>? actions))
			return;

		foreach (ActionID action in actions) {
			int held = buttonHeldCounts.TryGetValue(action, out int h) ? h : 0;
			if (nowDown) {
				// multibind: press fires for every newly pressed bound source
				buttonHeldCounts[action] = held + 1;
				events.Add(new ButtonActionEvent(tick, action, EdgeType.Press, info));
			} else {
				// multibind: release only fires when all bound sources are up
				int next = Math.Max(held - 1, 0);
				buttonHeldCounts[action] = next;
				if (held > 0 && next == 0)
					events.Add(new ButtonActionEvent(tick, action, EdgeType.Release, info));
			}
		}
	}

	private void handlePointerWheel(PointerWheelEvent wheel) {
		handleImpulseAxis(wheel.Tick, InputImpulseAxisSource.PointerWheel(PointerWheelAxis.X), wheel.X,
			ImpulseAxisActionEventInfo.FromPointer(wheel.MouseX, wheel.MouseY, wheel.IntegerX));
		handleImpulseAxis(wheel.Tick, InputImpulseAxisSource.PointerWheel(PointerWheelAxis.Y), wheel.Y,
			ImpulseAxisActionEventInfo.FromPointer(wheel.MouseX, wheel.MouseY, wheel.IntegerY));
	}

	private void handleImpulseAxis(MonoTick tick, InputImpulseAxisSource source, float amount, ImpulseAxisActionEventInfo info) {
		if (amount == 0f)
			return;
		if (!lookup.ImpulseAxesBySource.TryGetValue(source, out List<ImpulseAxisBinding>? bindings))
			return;
		foreach (ImpulseAxisBinding b in bindings) {
			float scaled = amount * b.Scale;
			if (scaled == 0f)
				continue;

			float cur = stepImpulseAmounts.TryGetValue(b.Action, out float old) ? old : 0f;
			stepImpulseAmounts[b.Action] = cur + scaled;
			events.Add(new ImpulseAxisActionEvent(tick, b.Action, scaled, info));
		}
	}

	private void evaluateButtonsFinal() {
		HashSet<ActionID> actions = new(lookup.ButtonActions);
		foreach (ActionID action in previousButtonDown.Keys)
			actions.Add(action);

		foreach (ActionID action in actions) {
			bool previous = previousButtonDown.TryGetValue(action, out bool p) && p;
			bool down = buttonHeldCounts.TryGetValue(action, out int held) && held > 0;
			buttonStates[action] = new ButtonActionState(down, previous);
			if (down)
				previousButtonDown[action] = true;
			else
				previousButtonDown.Remove(action);
		}
	}

	private void evaluateStateAxesFinal(MonoTick tick, InputSnapshot raw) {
		Dictionary<ActionID, float> nextValues = [];

		foreach (StateAxisBinding b in map.StateAxisBindings) {
			float val = getStateAxisValue(raw, b.Source) * b.Scale;
			nextValues.TryGetValue(b.Action, out float current);
			nextValues[b.Action] = mergeStateAxis(current, val, map.StateAxisMergePolicy);
		}

		HashSet<ActionID> actions = new(lookup.StateAxisActions);
		foreach (ActionID action in stateAxisValues.Keys)
			actions.Add(action);

		foreach (ActionID action in actions) {
			float old = stateAxisValues.TryGetValue(action, out float o) ? o : 0f;
			float next = nextValues.TryGetValue(action, out float n) ? n : 0f;
			stateAxisStates[action] = new StateAxisActionState(next, old);

			if (forceEmitStateAxes || old != next)
				events.Add(new StateAxisActionEvent(tick, action, next));

			if (next != 0f)
				stateAxisValues[action] = next;
			else
				stateAxisValues.Remove(action);
		}
	}

	private void evaluateStateAxes2DFinal(MonoTick tick, InputSnapshot raw) {
		Dictionary<ActionID, Vector2> nextValues = [];

		foreach (StateAxis2DBinding b in map.StateAxis2DBindings) {
			Vector2 v = getStateAxis2DValue(raw, b.Source);
			v = b.Deadzone.Apply(v);
			v *= b.Scale;
			v = clampMag1(v);

			nextValues.TryGetValue(b.Action, out Vector2 current);
			nextValues[b.Action] = mergeStateAxis2D(current, v, map.StateAxis2DMergePolicy);
		}

		HashSet<ActionID> actions = new(lookup.StateAxis2DActions);
		foreach (ActionID action in stateAxis2DValues.Keys)
			actions.Add(action);

		foreach (ActionID action in actions) {
			stateAxis2DValues.TryGetValue(action, out Vector2 old);
			nextValues.TryGetValue(action, out Vector2 next);

			stateAxis2DStates[action] = new StateAxis2DActionState(next, old);
			if (forceEmitStateAxes2D || !nearlyEqual(old, next))
				events.Add(new StateAxis2DActionEvent(tick, action, next));

			if (next != Vector2.Zero)
				stateAxis2DValues[action] = next;
			else
				stateAxis2DValues.Remove(action);
		}
	}

	private void evaluateImpulseAxesFinal() {
		foreach ((ActionID action, float amount) in stepImpulseAmounts)
			if (amount != 0f)
				impulseAxisStates[action] = new ImpulseAxisActionState(amount);
	}

	private float getStateAxisValue(InputSnapshot raw, InputStateAxisSource source) {
		return source.Kind.Tag switch {
			InputStateAxisSourceKind.Case.GamepadAxis => getAnyGamepadAxis(raw.Gamepads, source.GamepadAxisValue),
			InputStateAxisSourceKind.Case.DigitalPair => getDigitalAxisValue(source.DigitalValue),
			_ => throw new UnreachableException()
		};
	}

	private Vector2 getStateAxis2DValue(InputSnapshot raw, InputStateAxis2DSource source) {
		return source.Kind.Tag switch {
			InputStateAxis2DSourceKind.Case.GamepadStick => getAnyGamepadStick(raw.Gamepads, source.GamepadStickValue),
			InputStateAxis2DSourceKind.Case.DigitalButtons => getDigital2D(source.DigitalValue),
			InputStateAxis2DSourceKind.Case.Pair => getPair2D(raw, source.PairValue),
			_ => throw new UnreachableException()
		};
	}

	private Vector2 getPair2D(InputSnapshot raw, StateAxis2DPairSource source) {
		return new Vector2(
			getStateAxisValue(raw, source.X),
			getStateAxisValue(raw, source.Y)
		);
	}

	private Vector2 getDigital2D(DigitalAxis2DSource source) {
		float x = getDigitalAxisValue(source.Left, source.Right, source.XSOCD);
		float y = getDigitalAxisValue(source.Up, source.Down, source.YSOCD);
		return clampMag1(new Vector2(x, y));
	}

	private float getDigitalAxisValue(DigitalAxisSource source) => getDigitalAxisValue(source.Negative, source.Positive, source.SOCD);
	private float getDigitalAxisValue(InputButtonSource negative, InputButtonSource positive, SOCDPolicy socd) {
		bool neg = buttonSourceDown.TryGetValue(negative, out bool n) && n;
		bool pos = buttonSourceDown.TryGetValue(positive, out bool p) && p;

		if (neg && !pos)
			return -1.0f;
		if (pos && !neg)
			return 1.0f;
		if (!neg && !pos)
			return 0.0f;

		return socd.Tag switch {
			SOCDPolicy.Case.Last => socdLast(negative, positive),
			SOCDPolicy.Case.First => socdFirst(negative, positive),
			SOCDPolicy.Case.Neutral => 0f,
			SOCDPolicy.Case.Positive => 1f,
			SOCDPolicy.Case.Negative => -1f,
			_ => throw new UnreachableException()
		};
	}

	private float socdLast(InputButtonSource negative, InputButtonSource positive) {
		ulong negAt = buttonSourcePressedAt.TryGetValue(negative, out ulong n) ? n : throw new InternalStateException("negative source isn't down");
		ulong posAt = buttonSourcePressedAt.TryGetValue(positive, out ulong p) ? p : throw new InternalStateException("positive source isn't down");
		if (negAt == posAt)
			throw new InternalStateException("negative/positive sources ended up with the same stamp; either it's not getting incremented correctly or both directions somehow resolved to the same source");
		return negAt > posAt ? -1f : 1f;
	}

	private float socdFirst(InputButtonSource negative, InputButtonSource positive) {
		ulong negAt = buttonSourcePressedAt.TryGetValue(negative, out ulong n) ? n : throw new InternalStateException("negative source isn't down");
		ulong posAt = buttonSourcePressedAt.TryGetValue(positive, out ulong p) ? p : throw new InternalStateException("positive source isn't down");
		if (negAt == posAt)
			throw new InternalStateException("negative/positive sources ended up with the same stamp; either it's not getting incremented correctly or both directions somehow resolved to the same source");
		return negAt < posAt ? -1f : 1f;
	}

	private static bool isButtonSourceDown(InputSnapshot raw, InputButtonSource source) {
		return source.Kind.Tag switch {
			InputButtonSourceKind.Case.Key => raw.Keyboard.IsDown(source.KeyValue),
			InputButtonSourceKind.Case.PointerButton => raw.Pointer.IsDown(source.PointerButtonValue),
			InputButtonSourceKind.Case.GamepadButton => anyGamepadButtonDown(raw.Gamepads, source.GamepadButtonValue),
			_ => throw new UnreachableException()
		};
	}

	private static bool anyGamepadButtonDown(GamepadStateSet gamepads, GamepadButton button) {
		foreach (GamepadStateEntry ent in gamepads)
			if (ent.State.IsDown(button))
				return true;
		return false;
	}

	private static float getAnyGamepadAxis(GamepadStateSet gamepads, GamepadAxis axis) {
		float max = 0.0f;
		foreach (GamepadStateEntry ent in gamepads) {
			float v = ent.State.GetAxis(axis);
			if (MathF.Abs(v) > MathF.Abs(max))
				max = v;
		}
		return max;
	}

	private static Vector2 getAnyGamepadStick(GamepadStateSet gamepads, GamepadStick stick) {
		GamepadAxis xAxis = stick == GamepadStick.Left ? GamepadAxis.LeftX : GamepadAxis.RightX;
		GamepadAxis yAxis = stick == GamepadStick.Left ? GamepadAxis.LeftY : GamepadAxis.RightY;

		Vector2 max = Vector2.Zero;
		foreach (GamepadStateEntry ent in gamepads) {
			Vector2 v = new(ent.State.GetAxis(xAxis), ent.State.GetAxis(yAxis));
			if (v.LengthSquared() > max.LengthSquared())
				max = v;
		}
		return max;
	}

	private static float mergeStateAxis(float a, float b, StateAxisMergePolicy policy) {
		return policy.Tag switch {
			StateAxisMergePolicy.Case.MaxAbs => MathF.Abs(b) > MathF.Abs(a) ? b : a,
			StateAxisMergePolicy.Case.SumClamp => Math.Clamp(a + b, -1f, 1f),
			_ => throw new UnreachableException()
		};
	}

	private static Vector2 mergeStateAxis2D(Vector2 a, Vector2 b, StateAxis2DMergePolicy policy) {
		return policy.Tag switch {
			StateAxis2DMergePolicy.Case.MaxMagnitude => b.LengthSquared() > a.LengthSquared() ? b : a,
			StateAxis2DMergePolicy.Case.SumClamp => clampMag1(a + b),
			_ => throw new UnreachableException()
		};
	}

	private static Vector2 clampMag1(Vector2 v) {
		float lenSq = v.LengthSquared();
		if (lenSq <= 1f)
			return v;
		return v / MathF.Sqrt(lenSq);
	}

	private static bool nearlyEqual(Vector2 a, Vector2 b, float epsilon = 0.0001f) =>
		MathF.Abs(a.X - b.X) <= epsilon && MathF.Abs(a.Y - b.Y) <= epsilon;
}
