// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using Hexa.NET.SDL2;

using Injure.DataStructures;
using Injure.Timing;

namespace Injure.Input;

public readonly struct InputActionEvent(ActionID id, EdgeType edge, PerfTick perfTimestamp) {
	public readonly ActionID ID = id;
	public readonly EdgeType Edge = edge;
	public readonly PerfTick PerfTimestamp = perfTimestamp;
}

// TODO: keybind / controller bind creation system that Isn't this
public static class InputSystem {
	private static readonly FrozenSnapshotTwoWayMap<ActionID, string> map = new FrozenSnapshotTwoWayMap<ActionID, string>(cmpLeft: null, StringComparer.Ordinal);
	private static int next = -1;
	internal static int ActionCount => Volatile.Read(ref next) + 1;

	// note: this rebuilds the entire map (it's Frozen, what did you expect)
	public static ActionID Register(string sid) {
		ActionID id = new ActionID(Interlocked.Increment(ref next));
		map.Set(id, sid);
		return id;
	}

	// same note as above
	public static ActionID[] Register(params string[] sids) {
		if (sids.Length == 0)
			return Array.Empty<ActionID>();
		ActionID[] ids = new ActionID[sids.Length];
		for (int i = 0; i < sids.Length; i++)
			ids[i] = new ActionID(Interlocked.Increment(ref next));
		map.Set(ids, sids);
		return ids;
	}

	public static string QueryByID(ActionID id) => map.TryGetByLeft(id, out string? sid) ? sid : throw new KeyNotFoundException($"action with ID {id} not found");
	public static ActionID QueryBySID(string sid) => map.TryGetByRight(sid, out ActionID id) ? id : throw new KeyNotFoundException($"action with SID {sid} not found");

	// ====================
	// the dogshit zone
	private static readonly Dictionary<SDLScancode, ActionID> placeholderMapDict = new Dictionary<SDLScancode, ActionID>();

	public static void ThisMethodIsAPlaceholder_RegisterKeybind(SDLScancode scancode, ActionID actid) {
		placeholderMapDict.Add(scancode, actid);
	}

	internal static bool TryMapToAction(RawInputEvent r, out InputActionEvent ev) {
		// placeholder implementation
		if (placeholderMapDict.TryGetValue((SDLScancode)r.ID.Code, out ActionID actid)) {
			ev = new InputActionEvent(actid, r.Edge, r.PerfTimestamp);
			return true;
		}
		ev = default;
		return false;
	}
}
