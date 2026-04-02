// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;

namespace Injure.DataStructures;

// TODO: this needs to be less barebones, add at least PeekOldest/PeekNewest/PopOldest/PopNewest/Clear
public sealed class RingBuffer<T> : IReadOnlyList<T> {
	private readonly T[] buf;
	private int head;
	private int count;

	public T this[int idx] {
		get {
			ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)idx, (uint)count);
			return buf[(head + idx) % buf.Length];
		}
	}

	public int Capacity => buf.Length;
	public int Count => count;

	public RingBuffer(int capacity) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
		buf = new T[capacity];
	}

	public void Push(T item) {
		if (count < buf.Length) {
			buf[(head + count) % buf.Length] = item;
			count++;
		} else {
			buf[head] = item;
			head = (head + 1) % buf.Length;
		}
	}

	public Enumerator GetEnumerator() => new Enumerator(this);
	IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public struct Enumerator(RingBuffer<T> ringbuf) : IEnumerator<T> {
		private readonly RingBuffer<T> ringbuf = ringbuf;
		private int idx = -1;

		public readonly T Current {
			get {
				if ((uint)idx >= (uint)ringbuf.count)
					throw new InvalidOperationException();
				return ringbuf[idx];
			}
		}

		readonly object? IEnumerator.Current => Current;

		public bool MoveNext() {
			if (idx + 1 >= ringbuf.count)
				return false;
			idx++;
			return true;
		}

		public void Reset() {
			idx = -1;
		}

		public readonly void Dispose() {
		}
	}
}
