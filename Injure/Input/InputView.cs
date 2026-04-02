// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

namespace Injure.Input;

internal sealed class InputViewScratch {
	// 16 is a sane default
	public uint[] Pressed = new uint[16];
	public uint[] Released = new uint[16];
	public uint Counter; // TODO this variable name fucking sucks

	public void Ensure(int n) {
		if (Pressed.Length >= n)
			return;
		int newlen = Math.Max(Pressed.Length, 16);
		while (newlen < n)
			newlen *= 2;
		Array.Resize(ref Pressed, newlen);
		Array.Resize(ref Released, newlen);
	}

	public void Begin(int actionCount) {
		Ensure(actionCount);
		if (unchecked(++Counter) == 0) {
			Array.Clear(Pressed, 0, Pressed.Length);
			Array.Clear(Released, 0, Released.Length);
			Counter = 1;
		}
	}
}

public readonly ref struct InputView {
	private readonly ReadOnlySpan<InputActionEvent> events;
	private readonly uint[] pressed;
	private readonly uint[] released;
	private readonly uint counter; // TODO this variable name fucking sucks

	internal InputView(ReadOnlySpan<InputActionEvent> events, InputViewScratch scratch) {
		this.events = events;
		scratch.Begin(InputSystem.ActionCount);
		pressed = scratch.Pressed;
		released = scratch.Released;
		counter = scratch.Counter;
		foreach (InputActionEvent ev in events)
			(ev.Edge == EdgeType.Press ? pressed : released)[ev.ID.Val] = counter;
	}

	// simple input api
	public bool Pressed(ActionID id) => (uint)id.Val < (uint)pressed.Length && pressed[id.Val] == counter;
#pragma warning disable CA1822 // member can be made static
	public bool Down(ActionID id) => InputCollector.Down(id);
#pragma warning restore CA1822 // member can be made static
	public bool Released(ActionID id) => (uint)id.Val < (uint)released.Length && released[id.Val] == counter;

	// hi-res input api
	public ReadOnlySpan<InputActionEvent> Events => events;
	public void ForEachEvent(Action<InputActionEvent> fn, ActionID id, EdgeType? type = null) {
		foreach (InputActionEvent ev in events)
			if ((type is null || ev.Edge == type) && ev.ID == id)
				fn(ev);
	}
	public void ForEachEvent(Action<InputActionEvent> fn, IEnumerable<ActionID> ids, EdgeType? type = null) {
		ActionID[] arr = ids.ToArray(); // don't re-enumerate every single time
		foreach (InputActionEvent ev in events)
			if ((type is null || ev.Edge == type) && arr.Contains(ev.ID))
				fn(ev);
	}
}
