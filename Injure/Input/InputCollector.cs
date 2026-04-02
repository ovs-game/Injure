// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Injure.SourceGen;

namespace Injure.Input;

[StronglyTypedInt(typeof(ulong))]
internal readonly partial struct InputEventSeq {}

internal static class InputCollector {
	private static InputActionEvent[] actEvents = new InputActionEvent[64];
	private static int evCount = 0;

	private static InputEventSeq firstSeq = InputEventSeq.Zero;
	private static InputEventSeq nextSeq = InputEventSeq.Zero;

	private static readonly Dictionary<ActionID, bool> down = new Dictionary<ActionID, bool>();

	public static InputEventSeq NextSeq => nextSeq;
	public static int EvCount => evCount;

	public static void Feed(Queue<RawInputEvent> queue) {
		while (queue.Count != 0) {
			RawInputEvent raw = queue.Dequeue();
			if (!InputSystem.TryMapToAction(raw, out InputActionEvent ev))
				continue;
			if (evCount == actEvents.Length)
				Array.Resize(ref actEvents, actEvents.Length * 2);
			actEvents[evCount++] = ev;
			nextSeq++;
			down[ev.ID] = raw.Edge == EdgeType.Press;
		}
	}

	public static bool Down(ActionID id) => down[id];

	public static ReadOnlySpan<InputActionEvent> ReadSince(InputEventSeq seq) {
		if (!tryGetStartIndex(seq, out int startIndex))
			throw new InvalidOperationException("InputEventSeq has been invalidated");
		return actEvents.AsSpan(startIndex, evCount - startIndex);
	}

	public static ReadOnlySpan<InputActionEvent> ReadSinceAndAdvance(ref InputEventSeq seq) {
		ReadOnlySpan<InputActionEvent> events = ReadSince(seq);
		seq = nextSeq;
		return events;
	}

	public static void DiscardBefore(InputEventSeq seq) {
		if (seq <= firstSeq)
			return;

		if (seq >= nextSeq) {
			firstSeq = nextSeq;
			evCount = 0;
			return;
		}

		int drop = checked((int)(seq - firstSeq).Value);
		Array.Copy(actEvents, drop, actEvents, 0, evCount - drop);
		evCount -= drop;
		firstSeq = seq;
	}

	public static void DiscardAll() {
		firstSeq = nextSeq;
		evCount = 0;
	}

	public static void DiscardOldestToCount(int maxCount) {
		ArgumentOutOfRangeException.ThrowIfNegative(maxCount);
		if (evCount <= maxCount)
			return;
		int drop = evCount - maxCount;
		Array.Copy(actEvents, drop, actEvents, 0, maxCount);
		evCount = maxCount;
		firstSeq += (InputEventSeq)(ulong)drop;
	}

	private static bool tryGetStartIndex(InputEventSeq seq, out int startIndex) {
		if (seq < firstSeq || seq > nextSeq) {
			startIndex = 0;
			return false;
		}
		startIndex = checked((int)(seq - firstSeq).Value);
		return true;
	}
}
