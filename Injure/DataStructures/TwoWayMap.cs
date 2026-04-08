// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Injure.DataStructures;

public sealed class TwoWayMap<TLeft, TRight> : ITwoWayMap<TLeft, TRight> where TLeft : notnull where TRight : notnull {
	// ==========================================================================
	// bookkeeping
	private readonly Dictionary<TLeft, TRight> ltr;
	private readonly Dictionary<TRight, TLeft> rtl;

	// ==========================================================================
	// ctors
	public TwoWayMap(IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		ltr = new Dictionary<TLeft, TRight>(cmpLeft);
		rtl = new Dictionary<TRight, TLeft>(cmpRight);
	}

	public TwoWayMap(IEnumerable<(TLeft Left, TRight Right)> pairs, IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		(ltr, rtl) = make(pairs, cmpLeft, cmpRight);
	}

	public TwoWayMap(TLeft[] lefts, TRight[] rights, IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		(ltr, rtl) = make(lefts, rights, cmpLeft, cmpRight);
	}

	// ==========================================================================
	// private utility
	private static void add(Dictionary<TLeft, TRight> ltr, Dictionary<TRight, TLeft> rtl, TLeft left, TRight right) {
		if (ltr.ContainsKey(left))
			throw new ArgumentException("duplicate left key");
		if (rtl.ContainsKey(right))
			throw new ArgumentException("duplicate right key");
		ltr.Add(left, right);
		rtl.Add(right, left);
	}

	private static void set(Dictionary<TLeft, TRight> ltr, Dictionary<TRight, TLeft> rtl, TLeft left, TRight right) {
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

	private static (Dictionary<TLeft, TRight> LTR, Dictionary<TRight, TLeft> RTL) make(
		IEnumerable<(TLeft Left, TRight Right)> pairs, IEqualityComparer<TLeft>? cmpLeft, IEqualityComparer<TRight>? cmpRight) {
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(cmpLeft);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(cmpRight);
		foreach ((TLeft left, TRight right) in pairs)
			add(ltr, rtl, left, right);
		return (ltr, rtl);
	}

	private static (Dictionary<TLeft, TRight> LTR, Dictionary<TRight, TLeft> RTL) make(
		TLeft[] lefts, TRight[] rights, IEqualityComparer<TLeft>? cmpLeft, IEqualityComparer<TRight>? cmpRight) {
		if (lefts.Length != rights.Length)
			throw new ArgumentException("passed left<->right map arrays must be of equal length");
		Dictionary<TLeft, TRight> ltr = new Dictionary<TLeft, TRight>(lefts.Length, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new Dictionary<TRight, TLeft>(rights.Length, cmpRight);
		for (int i = 0; i < lefts.Length; i++)
			add(ltr, rtl, lefts[i], rights[i]);
		return (ltr, rtl);
	}

	// ==========================================================================
	// IReadOnlyTwoWayMap
	public int Count => ltr.Count;

	public IEnumerator<(TLeft Left, TRight Right)> GetEnumerator() {
		foreach (KeyValuePair<TLeft, TRight> kvp in ltr)
			yield return (kvp.Key, kvp.Value);
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public bool ContainsLeft(TLeft left) => ltr.ContainsKey(left);
	public bool ContainsRight(TRight right) => rtl.ContainsKey(right);
	public bool TryGetByLeft(TLeft left, [NotNullWhen(true)] out TRight? right) => ltr.TryGetValue(left, out right);
	public bool TryGetByRight(TRight right, [NotNullWhen(true)] out TLeft? left) => rtl.TryGetValue(right, out left);
	public TRight GetByLeft(TLeft left) => ltr[left];
	public TLeft GetByRight(TRight right) => rtl[right];

	// ==========================================================================
	// ITwoWayMap
	public void Clear() {
		ltr.Clear();
		rtl.Clear();
	}

	public void Add(TLeft left, TRight right) => add(ltr, rtl, left, right);

	public bool TryAdd(TLeft left, TRight right) {
		if (ltr.ContainsKey(left) || rtl.ContainsKey(right))
			return false;
		set(ltr, rtl, left, right);
		return true;
	}

	public void Set(TLeft left, TRight right) => set(ltr, rtl, left, right);

	public bool RemoveByLeft(TLeft left) => RemoveByLeft(left, out _);
	public bool RemoveByLeft(TLeft left, [NotNullWhen(true)] out TRight? right) {
		if (!ltr.TryGetValue(left, out right))
			return false;
		ltr.Remove(left);
		rtl.Remove(right);
		return true;
	}

	public bool RemoveByRight(TRight right) => RemoveByRight(right, out _);
	public bool RemoveByRight(TRight right, [NotNullWhen(true)] out TLeft? left) {
		if (!rtl.TryGetValue(right, out left))
			return false;
		rtl.Remove(right);
		ltr.Remove(left);
		return true;
	}
}
