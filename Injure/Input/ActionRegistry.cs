// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

using Injure.DataStructures;

namespace Injure.Input;

public sealed class ActionRegistry {
	// FrozenSnapshotTwoWayMap isn't safe for concurrent writes without external mutexing,
	// it won't corrupt state but if writes race only the winner's snapshot update makes
	// it in and the other changes get lost
	private readonly Lock writeLock = new Lock();

	private readonly FrozenSnapshotTwoWayMap<string, ActionID> actions = new FrozenSnapshotTwoWayMap<string, ActionID>(cmpLeft: StringComparer.Ordinal);
	private uint nextID = 0; // first will be 1 since this gets incremented upfront

	public ActionID Register(string sid) {
		ValidateSIDOrThrow(sid);
		lock (writeLock) {
			if (actions.ContainsLeft(sid))
				throw new InvalidOperationException($"action SID {sid} is already registered");
			ActionID id = new ActionID(nextID + 1);
			actions.Set(sid, id);
			nextID++;
			return id;
		}
	}

	public ActionID[] RegisterMany(ReadOnlySpan<string> sids) {
		// XXX: see above in Register, this has the same issue
		if (sids.Length == 0) // probably a bug on the caller side, throw
			throw new ArgumentException("SID list is empty, did you mean to pass in something else?", nameof(sids));

		HashSet<string> tmp = new HashSet<string>(StringComparer.Ordinal);
		foreach (string sid in sids) {
			ValidateSIDOrThrow(sid);
			if (!tmp.Add(sid))
				throw new ArgumentException("SID list must not contain duplicates", nameof(sids));
		}
		lock (writeLock) {
			foreach (string sid in sids)
				if (actions.ContainsLeft(sid))
					throw new InvalidOperationException($"action SID {sid} is already registered");
			ActionID[] ids = new ActionID[sids.Length];
			for (int i = 0; i < sids.Length; i++)
				ids[i] = new ActionID(nextID + 1 + (uint)i);
			actions.Set(sids, ids);
			nextID += (uint)sids.Length;
			return ids;
		}
	}

	public bool TryGetID(string sid, out ActionID id) => actions.TryGetByLeft(sid, out id);
	public bool TryGetSID(ActionID id, [NotNullWhen(true)] out string? sid) => actions.TryGetByRight(id, out sid);

	// these could just redirect to actions.GetBy* but this has nicer exception messages
	public ActionID GetID(string sid) {
		if (!actions.TryGetByLeft(sid, out ActionID id))
			throw new ArgumentException("unknown action SID", nameof(sid));
		return id;
	}
	public string GetSID(ActionID id) {
		if (!actions.TryGetByRight(id, out string? sid))
			throw new ArgumentException("unknown action ID", nameof(id));
		return sid;
	}

	public static bool ValidateSID([NotNullWhen(true)] string? sid, [NotNullWhen(false)] out string? err) {
		static bool validateSeg(ReadOnlySpan<char> s, string kind, [NotNullWhen(false)] out string? err) {
			if (s.IsEmpty) {
				err = $"action SID {kind} segment must not be empty";
				return false;
			}
			if (!char.IsAsciiLetterOrDigit(s[0])) {
				err = $"action SID {kind} segment must start with an ASCII letter or ASCII digit";
				return false;
			}
			foreach (char c in s) {
				if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '.')) {
					err = $"action SID contains invalid UTF-16 code unit U+{(ushort)c:X4} '{c}' (valid: ASCII letters, ASCII digits, '_', '-', '.')";
					return false;
				}
			}
			err = null;
			return true;
		}

		if (sid is null) {
			err = "action SID must not be null";
			return false;
		}
		if (sid.Length == 0) {
			err = "action SID must not be empty";
			return false;
		}
		int sep = sid.IndexOf("::", StringComparison.Ordinal);
		if (sep < 0 || sid.IndexOf("::", sep + 2, StringComparison.Ordinal) >= 0) {
			err = "action SID must contain exactly one occurrence of ::";
			return false;
		}
		if (!validateSeg(sid.AsSpan(0, sep), "namespace", out err) || !validateSeg(sid.AsSpan(sep + 2), "name", out err))
			return false;
		err = null;
		return true;
	}

	public static void ValidateSIDOrThrow([NotNull] string? sid) {
		if (!ValidateSID(sid, out string? err))
			throw new FormatException(err);
	}
}
