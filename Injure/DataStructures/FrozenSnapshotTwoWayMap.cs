// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Injure.DataStructures;

public sealed class FrozenSnapshotTwoWayMap<TLeft, TRight> : ITwoWayMap<TLeft, TRight> where TLeft : notnull where TRight : notnull {
	// ==========================================================================
	// abstraction over L/R pairs
	private readonly ref struct PairSource {
		private enum Kind : byte {
			PairSpan,
			SplitSpans
		}
		private readonly ReadOnlySpan<(TLeft Left, TRight Right)> pairs;
		private readonly ReadOnlySpan<TLeft> lefts;
		private readonly ReadOnlySpan<TRight> rights;
		private readonly Kind kind;

		public PairSource(ReadOnlySpan<(TLeft Left, TRight Right)> pairs) {
			this.pairs = pairs;
			lefts = default;
			rights = default;
			kind = Kind.PairSpan;
		}

		public PairSource(ReadOnlySpan<TLeft> lefts, ReadOnlySpan<TRight> rights) {
			if (lefts.Length != rights.Length)
				throw new ArgumentException("left/right counts must match");
			pairs = default;
			this.lefts = lefts;
			this.rights = rights;
			kind = Kind.SplitSpans;
		}

		public int Length => kind == Kind.PairSpan ? pairs.Length : lefts.Length;
		public (TLeft left, TRight right) Get(int idx) => kind == Kind.PairSpan ? pairs[idx] : (lefts[idx], rights[idx]);
	}

	// ==========================================================================
	// bookkeeping
	private sealed class Snapshot(FrozenDictionary<TLeft, TRight> ltr, FrozenDictionary<TRight, TLeft> rtl) {
		public readonly FrozenDictionary<TLeft, TRight> LTR = ltr;
		public readonly FrozenDictionary<TRight, TLeft> RTL = rtl;

		public static readonly Snapshot Empty = new Snapshot(FrozenDictionary<TLeft, TRight>.Empty, FrozenDictionary<TRight, TLeft>.Empty);
	}

	private readonly IEqualityComparer<TLeft>? cmpLeft;
	private readonly IEqualityComparer<TRight>? cmpRight;
	private Snapshot snapshot;

	// ==========================================================================
	// ctors
	public FrozenSnapshotTwoWayMap(IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		snapshot = Snapshot.Empty;
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	public FrozenSnapshotTwoWayMap(IEnumerable<(TLeft, TRight)> pairs, IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		snapshot = mksnap(pairs, cmpLeft, cmpRight);
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	public FrozenSnapshotTwoWayMap(ReadOnlySpan<(TLeft, TRight)> pairs, IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		snapshot = mksnap(new PairSource(pairs), cmpLeft, cmpRight);
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	public FrozenSnapshotTwoWayMap(ReadOnlySpan<TLeft> lefts, ReadOnlySpan<TRight> rights, IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		snapshot = mksnap(new PairSource(lefts, rights), cmpLeft, cmpRight);
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	// ==========================================================================
	// private utility
	private static Snapshot freeze(Dictionary<TLeft, TRight> ltr, Dictionary<TRight, TLeft> rtl, IEqualityComparer<TLeft>? cmpLeft, IEqualityComparer<TRight>? cmpRight) {
		return new Snapshot(
			ltr.Count == 0 ? FrozenDictionary<TLeft, TRight>.Empty : ltr.ToFrozenDictionary(cmpLeft),
			rtl.Count == 0 ? FrozenDictionary<TRight, TLeft>.Empty : rtl.ToFrozenDictionary(cmpRight)
		);
	}

	private static void add(Dictionary<TLeft, TRight> ltr, Dictionary<TRight, TLeft> rtl, TLeft left, TRight right) {
		if (ltr.ContainsKey(left))
			throw new ArgumentException("duplicate left key");
		if (rtl.ContainsKey(right))
			throw new ArgumentException("duplicate right key");
		ltr.Add(left, right);
		rtl.Add(right, left);
	}

	private static void setBijection(Dictionary<TLeft, TRight> ltr, Dictionary<TRight, TLeft> rtl, TLeft left, TRight right) {
		if (ltr.TryGetValue(left, out TRight? oldRight)) {
			if (rtl.Comparer.Equals(oldRight, right))
				return;
			rtl.Remove(oldRight);
		}
		if (rtl.TryGetValue(right, out TLeft? oldLeft))
			if (!ltr.Comparer.Equals(oldLeft, left))
				ltr.Remove(oldLeft);
		ltr[left] = right;
		rtl[right] = left;
	}

	private static Snapshot mksnap(IEnumerable<(TLeft, TRight)> pairs, IEqualityComparer<TLeft>? cmpLeft, IEqualityComparer<TRight>? cmpRight) {
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(cmpLeft);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(cmpRight);
		foreach ((TLeft left, TRight right) in pairs)
			add(ltr, rtl, left, right);
		return freeze(ltr, rtl, cmpLeft, cmpRight);
	}

	private static Snapshot mksnap(PairSource pairs, IEqualityComparer<TLeft>? cmpLeft, IEqualityComparer<TRight>? cmpRight) {
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(cmpLeft);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(cmpRight);
		for (int i = 0; i < pairs.Length; i++) {
			(TLeft left, TRight right) = pairs.Get(i);
			add(ltr, rtl, left, right);
		}
		return freeze(ltr, rtl, cmpLeft, cmpRight);
	}

	// ==========================================================================
	// IReadOnlyTwoWayMap
	public int Count {
		get {
			Snapshot s = Volatile.Read(ref snapshot);
			return s.LTR.Count;
		}
	}

	public IEnumerator<(TLeft Left, TRight Right)> GetEnumerator() {
		Snapshot s = Volatile.Read(ref snapshot);
		foreach (KeyValuePair<TLeft, TRight> kvp in s.LTR)
			yield return (kvp.Key, kvp.Value);
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public bool ContainsLeft(TLeft left) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.LTR.ContainsKey(left);
	}

	public bool ContainsRight(TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.RTL.ContainsKey(right);
	}

	public TRight GetByLeft(TLeft left) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.LTR[left];
	}

	public TLeft GetByRight(TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.RTL[right];
	}

	public bool TryGetByLeft(TLeft left, [NotNullWhen(true)] out TRight? right) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.LTR.TryGetValue(left, out right);
	}

	public bool TryGetByRight(TRight right, [NotNullWhen(true)] out TLeft? left) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.RTL.TryGetValue(right, out left);
	}

	// ==========================================================================
	// ITwoWayMap
	public void Clear() => Volatile.Write(ref snapshot, Snapshot.Empty);

	public void Add(TLeft left, TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		if (s.LTR.ContainsKey(left))
			throw new InvalidOperationException("this left key is already in the map");
		if (s.RTL.ContainsKey(right))
			throw new InvalidOperationException("this right key is already in the map");
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(s.LTR, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(s.RTL, cmpRight);
		setBijection(ltr, rtl, left, right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	public bool TryAdd(TLeft left, TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		if (s.LTR.ContainsKey(left) || s.RTL.ContainsKey(right))
			return false;
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(s.LTR, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(s.RTL, cmpRight);
		setBijection(ltr, rtl, left, right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
		return true;
	}

	public void Set(TLeft left, TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(s.LTR, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(s.RTL, cmpRight);
		setBijection(ltr, rtl, left, right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	public bool RemoveByLeft(TLeft left) => RemoveByLeft(left, out _);
	public bool RemoveByLeft(TLeft left, [NotNullWhen(true)] out TRight? right) {
		Snapshot s = Volatile.Read(ref snapshot);
		if (!s.LTR.TryGetValue(left, out right))
			return false;
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(s.LTR);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(s.RTL);
		ltr.Remove(left);
		rtl.Remove(right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
		return true;
	}

	public bool RemoveByRight(TRight right) => RemoveByRight(right, out _);
	public bool RemoveByRight(TRight right, [NotNullWhen(true)] out TLeft? left) {
		Snapshot s = Volatile.Read(ref snapshot);
		if (!s.RTL.TryGetValue(right, out left))
			return false;
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(s.LTR);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(s.RTL);
		ltr.Remove(left);
		rtl.Remove(right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
		return true;
	}

	// ==========================================================================
	// bulk ops
	public void ReplaceContents(IEnumerable<(TLeft, TRight)> pairs) {
		Snapshot @new = mksnap(pairs, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	public void ReplaceContents(ReadOnlySpan<(TLeft, TRight)> pairs) {
		Snapshot @new = mksnap(new PairSource(pairs), cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	public void ReplaceContents(ReadOnlySpan<TLeft> lefts, ReadOnlySpan<TRight> rights) {
		Snapshot @new = mksnap(new PairSource(lefts, rights), cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	private void add(PairSource pairs) {
		Snapshot s = Volatile.Read(ref snapshot);
		HashSet<TLeft> seenLeft = new HashSet<TLeft>(cmpLeft);
		HashSet<TRight> seenRight = new HashSet<TRight>(cmpRight);
		for (int i = 0; i < pairs.Length; i++) {
			(TLeft left, TRight right) = pairs.Get(i);
			if (s.LTR.ContainsKey(left))
				throw new InvalidOperationException($"one of the left keys is already in the map (index {i} in the given list)");
			if (s.RTL.ContainsKey(right))
				throw new InvalidOperationException($"one of the right keys is already in the map (index {i} in the given list)");
			if (!seenLeft.Add(left))
				throw new InvalidOperationException($"duplicate left key in the given list (index {i})");
			if (!seenRight.Add(right))
				throw new InvalidOperationException($"duplicate right key in the given list (index {i})");
		}

		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(s.LTR, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(s.RTL, cmpRight);
		for (int i = 0; i < pairs.Length; i++) {
			(TLeft left, TRight right) = pairs.Get(i);
			ltr.Add(left, right);
			rtl.Add(right, left);
		}
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}
	public void Add(ReadOnlySpan<(TLeft, TRight)> pairs) => add(new PairSource(pairs));
	public void Add(ReadOnlySpan<TLeft> lefts, ReadOnlySpan<TRight> rights) => add(new PairSource(lefts, rights));

	private void set(PairSource pairs) {
		Snapshot s = Volatile.Read(ref snapshot);
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(s.LTR, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(s.RTL, cmpRight);
		for (int i = 0; i < pairs.Length; i++) {
			(TLeft left, TRight right) = pairs.Get(i);
			setBijection(ltr, rtl, left, right);
		}
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}
	public void Set(ReadOnlySpan<(TLeft, TRight)> pairs) => set(new PairSource(pairs));
	public void Set(ReadOnlySpan<TLeft> lefts, ReadOnlySpan<TRight> rights) => set(new PairSource(lefts, rights));

	// TODO: bulk-remove
}
