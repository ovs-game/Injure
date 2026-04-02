// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Injure.DataStructures;

public sealed class FrozenTwoWayMap<TLeft, TRight> : IReadOnlyCollection<(TLeft Left, TRight Right)> where TLeft : notnull where TRight : notnull {
	private sealed class Snapshot(FrozenDictionary<TLeft, TRight> ltr, FrozenDictionary<TRight, TLeft> rtl) {
		public readonly FrozenDictionary<TLeft, TRight> LTR = ltr;
		public readonly FrozenDictionary<TRight, TLeft> RTL = rtl;

		public static readonly Snapshot Empty = new Snapshot(FrozenDictionary<TLeft, TRight>.Empty, FrozenDictionary<TRight, TLeft>.Empty);
	}

	private readonly IEqualityComparer<TLeft>? cmpLeft;
	private readonly IEqualityComparer<TRight>? cmpRight;
	private Snapshot snapshot;

	public int Count {
		get {
			Snapshot s = Volatile.Read(ref snapshot);
			return s.LTR.Count;
		}
	}

	public FrozenTwoWayMap(IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		snapshot = Snapshot.Empty;
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	public FrozenTwoWayMap(IEnumerable<(TLeft, TRight)> pairs, IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		snapshot = mksnap(pairs, cmpLeft, cmpRight);
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	public FrozenTwoWayMap(TLeft[] lefts, TRight[] rights, IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		snapshot = mksnap(lefts, rights, cmpLeft, cmpRight);
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	private static Snapshot freeze(Dictionary<TLeft, TRight> ltr, Dictionary<TRight, TLeft> rtl, IEqualityComparer<TLeft>? cmpLeft, IEqualityComparer<TRight>? cmpRight) {
		return new Snapshot(
			ltr.Count == 0 ? FrozenDictionary<TLeft, TRight>.Empty : ltr.ToFrozenDictionary(cmpLeft),
			rtl.Count == 0 ? FrozenDictionary<TRight, TLeft>.Empty : rtl.ToFrozenDictionary(cmpRight)
		);
	}

	private static Snapshot mksnap(IEnumerable<(TLeft, TRight)> pairs, IEqualityComparer<TLeft>? cmpLeft, IEqualityComparer<TRight>? cmpRight) {
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(cmpLeft);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(cmpRight);
		foreach ((TLeft left, TRight right) in pairs) {
			if (ltr.ContainsKey(left))
				throw new ArgumentException("duplicate left key");
			if (rtl.ContainsKey(right))
				throw new ArgumentException("duplicate right key");

			ltr.Add(left, right);
			rtl.Add(right, left);
		}
		return freeze(ltr, rtl, cmpLeft, cmpRight);
	}

	private static Snapshot mksnap(TLeft[] lefts, TRight[] rights, IEqualityComparer<TLeft>? cmpLeft, IEqualityComparer<TRight>? cmpRight) {
		if (lefts.Length != rights.Length)
			throw new ArgumentException("passed left<->right map arrays must be of equal length");
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(cmpLeft);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(cmpRight);
		for (int i = 0; i < lefts.Length; i++) {
			if (ltr.ContainsKey(lefts[i]))
				throw new ArgumentException("duplicate left key");
			if (rtl.ContainsKey(rights[i]))
				throw new ArgumentException("duplicate right key");

			ltr.Add(lefts[i], rights[i]);
			rtl.Add(rights[i], lefts[i]);
		}
		return freeze(ltr, rtl, cmpLeft, cmpRight);
	}

	public IEnumerator<(TLeft Left, TRight Right)> GetEnumerator() {
		Snapshot s = Volatile.Read(ref snapshot);
		foreach (KeyValuePair<TLeft, TRight> kvp in s.LTR)
			yield return (kvp.Key, kvp.Value);
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public bool TryGetByLeft(TLeft left, [NotNullWhen(true)] out TRight? right) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.LTR.TryGetValue(left, out right);
	}

	public bool TryGetByRight(TRight right, [NotNullWhen(true)] out TLeft? left) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.RTL.TryGetValue(right, out left);
	}

	public void ReplaceWith(IEnumerable<(TLeft, TRight)> pairs) {
		Snapshot @new = mksnap(pairs, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	public void ReplaceWith(TLeft[] lefts, TRight[] rights) {
		Snapshot @new = mksnap(lefts, rights, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	public void Set(TLeft left, TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(s.LTR);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(s.RTL);
		if (ltr.TryGetValue(left, out TRight? oldright))
			rtl.Remove(oldright);
		if (rtl.TryGetValue(right, out TLeft? oldleft))
			ltr.Remove(oldleft);
		ltr[left] = right;
		rtl[right] = left;
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	public void Set(TLeft[] lefts, TRight[] rights) {
		if (lefts.Length != rights.Length)
			throw new ArgumentException("passed left<->right map arrays must be of equal length");
		Snapshot s = Volatile.Read(ref snapshot);
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(s.LTR);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(s.RTL);
		for (int i = 0; i < lefts.Length; i++) {
			if (ltr.TryGetValue(lefts[i], out TRight? oldright))
				rtl.Remove(oldright);
			if (rtl.TryGetValue(rights[i], out TLeft? oldleft))
				ltr.Remove(oldleft);
			ltr[lefts[i]] = rights[i];
			rtl[rights[i]] = lefts[i];
		}
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	public bool RemoveByLeft(TLeft left) {
		Snapshot s = Volatile.Read(ref snapshot);
		if (!s.LTR.TryGetValue(left, out TRight? right))
			return false;
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(s.LTR);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(s.RTL);
		ltr.Remove(left);
		rtl.Remove(right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
		return true;
	}

	public bool RemoveByRight(TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		if (!s.RTL.TryGetValue(right, out TLeft? left))
			return false;
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(s.LTR);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(s.RTL);
		ltr.Remove(left);
		rtl.Remove(right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
		return true;
	}
}
