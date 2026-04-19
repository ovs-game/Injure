// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Injure.Input;

public sealed class ActionMapSnapshot {
	private readonly ImmutableArray<ButtonBinding> buttonBindings;
	private readonly ImmutableArray<StateAxisBinding> stateAxisBindings;
	private readonly ImmutableArray<StateAxis2DBinding> stateAxis2DBindings;
	private readonly ImmutableArray<ImpulseAxisBinding> impulseAxisBindings;

	public IReadOnlyList<ButtonBinding> ButtonBindings => buttonBindings;
	public IReadOnlyList<StateAxisBinding> StateAxisBindings => stateAxisBindings;
	public IReadOnlyList<StateAxis2DBinding> StateAxis2DBindings => stateAxis2DBindings;
	public IReadOnlyList<ImpulseAxisBinding> ImpulseAxisBindings => impulseAxisBindings;

	public StateAxisMergePolicy StateAxisMergePolicy { get; }
	public StateAxis2DMergePolicy StateAxis2DMergePolicy { get; }

	public ActionMapSnapshot(
		ReadOnlySpan<ButtonBinding> buttonBindings,
		ReadOnlySpan<StateAxisBinding> stateAxisBindings,
		ReadOnlySpan<StateAxis2DBinding> stateAxis2DBindings,
		ReadOnlySpan<ImpulseAxisBinding> impulseAxisBindings,
		StateAxisMergePolicy stateAxisMergePolicy = StateAxisMergePolicy.MaxAbs,
		StateAxis2DMergePolicy stateAxis2DMergePolicy = StateAxis2DMergePolicy.MaxMagnitude
	) {
		this.buttonBindings = validate(buttonBindings, b => (b.Action, b.Source), "button");
		this.stateAxisBindings = validate(stateAxisBindings, b => (b.Action, b.Source), "state axis");
		this.stateAxis2DBindings = validate(stateAxis2DBindings, b => (b.Action, b.Source), "2D state axis");
		this.impulseAxisBindings = validate(impulseAxisBindings, b => (b.Action, b.Source), "impulse axis");
		StateAxisMergePolicy = stateAxisMergePolicy;
		StateAxis2DMergePolicy = stateAxis2DMergePolicy;
	}

	private static ImmutableArray<TBinding> validate<TBinding, TSource>(ReadOnlySpan<TBinding> bindings,
		Func<TBinding, (ActionID, TSource)> getData, string kind) {
		HashSet<(ActionID, TSource)> seen = new HashSet<(ActionID, TSource)>();
		foreach (TBinding b in bindings)
			if (!seen.Add(getData(b)))
				throw new ArgumentException($"{kind} bindings must not contain duplicate action/source pairs");
		return bindings.ToImmutableArray();
	}
}

public sealed class ActionMapBuilder {
	private readonly List<ButtonBinding> buttonBindings = new List<ButtonBinding>();
	private readonly List<StateAxisBinding> stateAxisBindings = new List<StateAxisBinding>();
	private readonly List<StateAxis2DBinding> stateAxis2DBindings = new List<StateAxis2DBinding>();
	private readonly List<ImpulseAxisBinding> impulseAxisBindings = new List<ImpulseAxisBinding>();

	public IReadOnlyList<ButtonBinding> ButtonBindings => buttonBindings;
	public IReadOnlyList<StateAxisBinding> StateAxisBindings => stateAxisBindings;
	public IReadOnlyList<StateAxis2DBinding> StateAxis2DBindings => stateAxis2DBindings;
	public IReadOnlyList<ImpulseAxisBinding> ImpulseAxisBindings => impulseAxisBindings;

	public StateAxisMergePolicy StateAxisMergePolicy { get; set; } = StateAxisMergePolicy.MaxAbs;
	public StateAxis2DMergePolicy StateAxis2DMergePolicy { get; set; } = StateAxis2DMergePolicy.MaxMagnitude;

	public static ActionMapBuilder FromSnapshot(ActionMapSnapshot snapshot) {
		ArgumentNullException.ThrowIfNull(snapshot);
		ActionMapBuilder b = new ActionMapBuilder() {
			StateAxisMergePolicy = snapshot.StateAxisMergePolicy,
			StateAxis2DMergePolicy = snapshot.StateAxis2DMergePolicy
		};
		b.buttonBindings.AddRange(snapshot.ButtonBindings);
		b.stateAxisBindings.AddRange(snapshot.StateAxisBindings);
		b.impulseAxisBindings.AddRange(snapshot.ImpulseAxisBindings);
		b.stateAxis2DBindings.AddRange(snapshot.StateAxis2DBindings);
		return b;
	}

	public void BindButton(ActionID action, InputButtonSource source) {
		ensureValid(action);
		foreach (ButtonBinding b in buttonBindings)
			if (b.Action == action && b.Source == source)
				throw new ArgumentException("duplicate button binding");
		buttonBindings.Add(new ButtonBinding(action, source));
	}

	public void BindStateAxis(ActionID action, InputStateAxisSource source, AxisDeadzone deadzone, float scale = 1f) {
		ensureValid(action);
		foreach (StateAxisBinding b in stateAxisBindings)
			if (b.Action == action && b.Source == source)
				throw new ArgumentException("duplicate state axis binding");
		stateAxisBindings.Add(new StateAxisBinding(action, source, deadzone, scale));
	}

	public void BindStateAxis2D(ActionID action, InputStateAxis2DSource source, Axis2DDeadzone deadzone) {
		ensureValid(action);
		foreach (StateAxis2DBinding b in stateAxis2DBindings)
			if (b.Action == action && b.Source == source)
				throw new ArgumentException("duplicate 2D state axis binding");
		stateAxis2DBindings.Add(new StateAxis2DBinding(action, source, deadzone, Vector2.One));
	}

	public void BindStateAxis2D(ActionID action, InputStateAxis2DSource source, Axis2DDeadzone deadzone, Vector2 scale) {
		ensureValid(action);
		foreach (StateAxis2DBinding b in stateAxis2DBindings)
			if (b.Action == action && b.Source == source)
				throw new ArgumentException("duplicate 2D state axis binding");
		stateAxis2DBindings.Add(new StateAxis2DBinding(action, source, deadzone, scale));
	}

	public void BindImpulseAxis(ActionID action, InputImpulseAxisSource source, float scale = 1f) {
		ensureValid(action);
		foreach (ImpulseAxisBinding b in impulseAxisBindings)
			if (b.Action == action && b.Source == source)
				throw new ArgumentException("duplicate impulse axis binding");
		impulseAxisBindings.Add(new ImpulseAxisBinding(action, source, scale));
	}

	public void ClearBindingsFor(ActionID action) {
		buttonBindings.RemoveAll(b => b.Action == action);
		stateAxisBindings.RemoveAll(b => b.Action == action);
		impulseAxisBindings.RemoveAll(b => b.Action == action);
		stateAxis2DBindings.RemoveAll(b => b.Action == action);
	}

	public void ClearButtonBindings(ActionID action) => buttonBindings.RemoveAll(b => b.Action == action);
	public void ClearStateAxisBindings(ActionID action) => stateAxisBindings.RemoveAll(b => b.Action == action);
	public void ClearImpulseAxisBindings(ActionID action) => impulseAxisBindings.RemoveAll(b => b.Action == action);
	public void ClearStateAxis2DBindings(ActionID action) => stateAxis2DBindings.RemoveAll(b => b.Action == action);

	public void Clear() {
		buttonBindings.Clear();
		stateAxisBindings.Clear();
		stateAxis2DBindings.Clear();
		impulseAxisBindings.Clear();
	}

	public ActionMapSnapshot ToSnapshot() {
		return new ActionMapSnapshot(
			CollectionsMarshal.AsSpan(buttonBindings),
			CollectionsMarshal.AsSpan(stateAxisBindings),
			CollectionsMarshal.AsSpan(stateAxis2DBindings),
			CollectionsMarshal.AsSpan(impulseAxisBindings),
			StateAxisMergePolicy,
			StateAxis2DMergePolicy
		);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ensureValid(ActionID action) {
		if (!action.IsValid)
			throw new ArgumentException("action ID must be valid", nameof(action));
	}
}

public sealed class ActionProfile(ActionMapSnapshot initial) {
	private ActionMapSnapshot current = initial ?? throw new ArgumentNullException(nameof(initial));
	private ulong version = 1;

	public ActionMapSnapshot Current => Volatile.Read(ref current);
	public ulong Version => Volatile.Read(ref version);

	public void Replace(ActionMapSnapshot next) {
		ArgumentNullException.ThrowIfNull(next);
		Volatile.Write(ref current, next);
		Interlocked.Increment(ref version);
	}
}
