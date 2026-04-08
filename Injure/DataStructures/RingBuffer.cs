// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Injure.DataStructures;

/// <summary>
/// Fixed-capacity deque-like ring buffer with a logical oldest-to-newest order.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>
/// <para>
/// Index <c>0</c> refers to the oldest element currently stored in the buffer, and index
/// <c><see cref="Count"/> - 1</c> refers to the newest.
/// </para>
/// <para>
/// The buffer has a fixed <see cref="Capacity"/>. Methods such as <see cref="PushNewest(T)"/>
/// and <see cref="PushOldest(T)"/> overwrite an existing element when the buffer is full.
/// The corresponding <c>TryNonOverwritingPush*</c> methods do not overwrite and instead
/// return <see langword="false"/>.
/// </para>
/// <para>
/// Standard enumeration goes from oldest to newest. Reverse enumeration is supported.
/// </para>
/// </remarks>
[DebuggerDisplay("Count = {Count}, Capacity = {Capacity}, Head = {head}")]
[DebuggerTypeProxy(typeof(RingBufferDebugView<>))]
public sealed class RingBuffer<T> : IReadOnlyList<T> {
	private readonly T[] buf;
	private int head;
	private int count;
	private ulong version = 0;

	/// <summary>
	/// Initializes a new ring buffer with the specified fixed capacity.
	/// </summary>
	/// <param name="capacity">The maximum number of elements the buffer can hold.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="capacity"/> is less than or equal to zero.
	/// </exception>
	public RingBuffer(int capacity) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
		buf = new T[capacity];
	}

	/// <summary>
	/// Initializes a new ring buffer with the specified fixed capacity and appends the provided items.
	/// </summary>
	/// <param name="capacity">The maximum number of elements the buffer can hold.</param>
	/// <param name="items">The items to append in enumeration order.</param>
	/// <remarks>
	/// If <paramref name="items"/> contains more than <paramref name="capacity"/> elements,
	/// the oldest excess elements are discarded according to normal overwrite semantics.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="capacity"/> is less than or equal to zero.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="items"/> is <see langword="null"/>.
	/// </exception>
	public RingBuffer(int capacity, IEnumerable<T> items) : this(capacity) {
		ArgumentNullException.ThrowIfNull(items);
		foreach (T item in items)
			PushNewest(item);
	}

	// ==========================================================================
	// private helpers
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void throwIfEmpty() {
		if (count == 0)
			throw new InvalidOperationException("ring buffer is empty");
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int physicalIndex(int logicalIndex) {
		int idx = head + logicalIndex;
		return idx < buf.Length ? idx : idx - buf.Length;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int incr(int idx) => idx + 1 == buf.Length ? 0 : idx + 1;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int decr(int idx) => idx == 0 ? buf.Length - 1 : idx - 1;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void clearSlot(int idx) {
		if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
			buf[idx] = default!;
	}

	// ==========================================================================
	// public api

	/// <summary>
	/// The maximum number of elements the buffer can hold.
	/// </summary>
	public int Capacity => buf.Length;

	/// <summary>
	/// Gets the number of elements currently stored in the buffer.
	/// </summary>
	public int Count => count;

	/// <summary>
	/// Whether the buffer currently contains no elements.
	/// </summary>
	public bool IsEmpty => count == 0;

	/// <summary>
	/// Whether the buffer is currently at full capacity.
	/// </summary>
	public bool IsFull => count == buf.Length;

	/// <summary>
	/// Gets the element at the specified logical index.
	/// </summary>
	/// <param name="idx">
	/// The logical index, where <c>0</c> is the oldest element.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="idx"/> is outside the valid range.
	/// </exception>
	public T this[int idx] {
		get {
			ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)idx, (uint)count);
			return buf[(head + idx) % buf.Length];
		}
	}

	/// <summary>
	/// Returns the oldest element in the buffer without removing it.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer is empty.
	/// </exception>
	public T PeekOldest() {
		throwIfEmpty();
		return buf[head];
	}

	/// <summary>
	/// Returns the newest element in the buffer without removing it.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer is empty.
	/// </exception>
	public T PeekNewest() {
		throwIfEmpty();
		return buf[physicalIndex(count - 1)];
	}

	/// <summary>
	/// Attempts to return the oldest element in the buffer without removing it.
	/// </summary>
	/// <param name="item">
	/// If the method returned <see langword="true"/>, the oldest element; otherwise,
	/// the default value of <typeparamref name="T"/>.
	/// </param>
	/// <returns><see langword="true"/> if the buffer is non-empty; otherwise, <see langword="false"/>.</returns>
	public bool TryPeekOldest([MaybeNullWhen(false)] out T item) {
		if (count == 0) {
			item = default;
			return false;
		}
		item = buf[head];
		return true;
	}

	/// <summary>
	/// Attempts to return the newest element in the buffer without removing it.
	/// </summary>
	/// <param name="item">
	/// If the method returned <see langword="true"/>, the newest element; otherwise,
	/// the default value of <typeparamref name="T"/>.
	/// </param>
	/// <returns><see langword="true"/> if the buffer is non-empty; otherwise, <see langword="false"/>.</returns>
	public bool TryPeekNewest([MaybeNullWhen(false)] out T item) {
		if (count == 0) {
			item = default;
			return false;
		}
		item = buf[physicalIndex(count - 1)];
		return true;
	}

	/// <summary>
	/// Appends an element at the logical end of the buffer.
	/// </summary>
	/// <param name="item">The element to append.</param>
	/// <remarks>
	/// If the buffer is full, the oldest element is overwritten.
	/// </remarks>
	public void PushNewest(T item) {
		if (count < buf.Length) {
			buf[physicalIndex(count)] = item;
			count++;
		} else {
			buf[head] = item;
			head = incr(head);
		}
		version++;
	}

	/// <summary>
	/// Prepends an element at the logical beginning of the buffer.
	/// </summary>
	/// <param name="item">The element to prepend.</param>
	/// <remarks>
	/// If the buffer is full, the newest element is overwritten.
	/// </remarks>
	public void PushOldest(T item) {
		head = decr(head);
		buf[head] = item;
		if (count < buf.Length)
			count++;
		version++;
	}

	/// <summary>
	/// Attempts to append an element at the logical end of the buffer.
	/// </summary>
	/// <param name="item">The element to append.</param>
	/// <returns>
	/// <see langword="true"/> if the element was appended; <see langword="false"/> if the buffer was full.
	/// </returns>
	public bool TryNonOverwritingPushNewest(T item) {
		if (count == buf.Length)
			return false;
		buf[physicalIndex(count)] = item;
		count++;
		version++;
		return true;
	}

	/// <summary>
	/// Attempts to prepend an element at the logical beginning of the buffer.
	/// </summary>
	/// <param name="item">The element to prepend.</param>
	/// <returns>
	/// <see langword="true"/> if the element was prepended; <see langword="false"/> if the buffer was full.
	/// </returns>
	public bool TryNonOverwritingPushOldest(T item) {
		if (count == buf.Length)
			return false;
		head = decr(head);
		buf[head] = item;
		count++;
		version++;
		return true;
	}

	/// <summary>
	/// Removes and returns the oldest element in the buffer.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer is empty.
	/// </exception>
	public T PopOldest() {
		throwIfEmpty();
		T item = buf[head];
		clearSlot(head);
		head = incr(head);
		if (--count == 0)
			head = 0;
		version++;
		return item;
	}

	/// <summary>
	/// Removes and returns the newest element in the buffer.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer is empty.
	/// </exception>
	public T PopNewest() {
		throwIfEmpty();
		int index = physicalIndex(count - 1);
		T item = buf[index];
		clearSlot(index);
		if (--count == 0)
			head = 0;
		version++;
		return item;
	}

	/// <summary>
	/// Attempts to remove and return the oldest element in the buffer.
	/// </summary>
	/// <param name="item">
	/// If the method returned <see langword="true"/>, the removed element; otherwise,
	/// the default value of <typeparamref name="T"/>.
	/// </param>
	/// <returns><see langword="true"/> if the buffer is non-empty; otherwise, <see langword="false"/>.</returns>
	public bool TryPopOldest([MaybeNullWhen(false)] out T item) {
		if (count == 0) {
			item = default;
			return false;
		}
		item = buf[head];
		clearSlot(head);
		head = incr(head);
		if (--count == 0)
			head = 0;
		version++;
		return true;
	}

	/// <summary>
	/// Attempts to remove and return the newest element in the buffer.
	/// </summary>
	/// <param name="item">
	/// If the method returned <see langword="true"/>, the removed element; otherwise,
	/// the default value of <typeparamref name="T"/>.
	/// </param>
	/// <returns><see langword="true"/> if the buffer is non-empty; otherwise, <see langword="false"/>.</returns>
	public bool TryPopNewest([MaybeNullWhen(false)] out T item) {
		if (count == 0) {
			item = default;
			return false;
		}
		int index = physicalIndex(count - 1);
		item = buf[index];
		clearSlot(index);
		if (--count == 0)
			head = 0;
		version++;
		return true;
	}

	/// <summary>
	/// Removes all elements from the buffer.
	/// </summary>
	/// <remarks>
	/// The capacity of the buffer is unchanged.
	/// </remarks>
	public void Clear() {
		if (count != 0 && RuntimeHelpers.IsReferenceOrContainsReferences<T>()) {
			int firstPart = Math.Min(count, buf.Length - head);
			Array.Clear(buf, head, firstPart);
			Array.Clear(buf, 0, count - firstPart);
		}
		head = 0;
		count = 0;
		version++;
	}

	/// <summary>
	/// Determines whether the buffer contains the specified element, using
	/// the specified equality comparer for the type.
	/// </summary>
	public bool Contains(T item, EqualityComparer<T> comparer) => IndexOf(item, comparer) >= 0;

	/// <summary>
	/// Determines whether the buffer contains the specified element, using
	/// the default equality comparer for the type.
	/// </summary>
	public bool Contains(T item) => IndexOf(item) >= 0;

	/// <summary>
	/// Returns the logical index of the first occurrence of the specified element,
	/// using the specified equality comparer for the type.
	/// </summary>
	public int IndexOf(T item, EqualityComparer<T> comparer) {
		int firstPart = Math.Min(count, buf.Length - head);
		for (int i = 0; i < firstPart; i++)
			if (comparer.Equals(buf[head + i], item))
				return i;
		for (int i = 0; i < count - firstPart; i++)
			if (comparer.Equals(buf[i], item))
				return firstPart + i;
		return -1;
	}

	/// <summary>
	/// Returns the logical index of the first occurrence of the specified element,
	/// using the default equality comparer for the type.
	/// </summary>
	public int IndexOf(T item) => IndexOf(item, EqualityComparer<T>.Default);

	/// <summary>
	/// Copies the contents of the buffer into the specified array.
	/// </summary>
	/// <param name="dst">The destination array.</param>
	/// <param name="dstOffset">
	/// The index in <paramref name="dst"/> at which copying begins, i.e the
	/// index to which the first element will be written.
	/// </param>
	/// <remarks>
	/// Elements are copied in the buffer's enumeration order (oldest to newest).
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="dst"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="dst"/> is negative.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="dstOffset"/> is out of bounds of <paramref name="dst"/>,
	/// or if <paramref name="dst"/> is too small to recieve all of the elements.
	/// </exception>
	public void CopyTo(T[] dst, int dstOffset = 0) {
		ArgumentNullException.ThrowIfNull(dst);
		ArgumentOutOfRangeException.ThrowIfNegative(dstOffset);
		if (dstOffset > dst.Length)
			throw new ArgumentException($"destination offset is out of bounds (dst length = {dst.Length}, offset = {dstOffset})", nameof(dst));
		if (dst.Length - dstOffset < count)
			throw new ArgumentException($"destination array is too small (length - offset = {dst.Length - dstOffset}, expected at least {count}", nameof(dst));
		CopyTo(dst.AsSpan(dstOffset));
	}

	/// <summary>
	/// Copies the contents of the buffer into the specified span.
	/// </summary>
	/// <param name="dst">The destination span.</param>
	/// <remarks>
	/// Elements are copied in the buffer's enumeration order (oldest to newest).
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="dst"/> is too small to be able to hold
	/// <see cref="Count"/> elements.
	/// </exception>
	public void CopyTo(Span<T> dst) {
		if (dst.Length < count)
			throw new ArgumentException($"destination span is too small (length = {dst.Length}, expected at least {count})", nameof(dst));
		int firstPart = Math.Min(count, buf.Length - head);
		buf.AsSpan(head, firstPart).CopyTo(dst);
		if (firstPart < count)
			buf.AsSpan(0, count - firstPart).CopyTo(dst[firstPart..]);
	}

	/// <summary>
	/// Returns a new array containing the contents of the buffer.
	/// </summary>
	/// <remarks>
	/// Elements are in the buffer's enumeration order (oldest to newest).
	/// </remarks>
	public T[] ToArray() {
		if (count == 0)
			return Array.Empty<T>();
		T[] arr = new T[count];
		CopyTo(arr);
		return arr;
	}

	/// <summary>
	/// Returns up to two contiguous spans that together represent the current logical contents.
	/// </summary>
	/// <param name="first">
	/// The first contiguous span of elements.
	/// </param>
	/// <param name="second">
	/// If the contents wrap around the end of the backing array, the second contiguous span
	/// of elements; otherwise, an empty span.
	/// </param>
	/// <remarks>
	/// <para>
	/// The returned spans are views into the live backing storage. Any subsequent mutation
	/// of the buffer may change the values visible through them or cause them to no longer
	/// represent the current logical contents.
	/// </para>
	/// <para>
	/// See <see cref="GetMemory(out ReadOnlyMemory{T}, out ReadOnlyMemory{T})"/> for a similar
	/// method that returns <see cref="ReadOnlyMemory{T}"/> regions instead.
	/// </para>
	/// </remarks>
	public void GetSegments(out ReadOnlySpan<T> first, out ReadOnlySpan<T> second) {
		if (count == 0) {
			first = second = ReadOnlySpan<T>.Empty;
			return;
		}
		int firstCount = Math.Min(count, buf.Length - head);
		first = buf.AsSpan(head, firstCount);
		second = buf.AsSpan(0, count - firstCount);
	}

	/// <summary>
	/// Returns up to two contiguous memory regions that together represent the current logical contents.
	/// </summary>
	/// <param name="first">
	/// The first contiguous region of elements.
	/// </param>
	/// <param name="second">
	/// If the contents wrap around the end of the backing array, the second contiguous region
	/// of elements; otherwise, an empty region.
	/// </param>
	/// <remarks>
	/// <para>
	/// The returned regions are views into the live backing storage. Any subsequent mutation
	/// of the buffer may change the values visible through them or cause them to no longer
	/// represent the current logical contents.
	/// </para>
	/// <para>
	/// See <see cref="GetSegments(out ReadOnlySpan{T}, out ReadOnlySpan{T})"/> for a similar
	/// method that returns spans instead.
	/// </para>
	/// </remarks>
	public void GetMemory(out ReadOnlyMemory<T> first, out ReadOnlyMemory<T> second) {
		if (count == 0) {
			first = second = ReadOnlyMemory<T>.Empty;
			return;
		}
		int firstCount = Math.Min(count, buf.Length - head);
		first = new ReadOnlyMemory<T>(buf, head, firstCount);
		second = new ReadOnlyMemory<T>(buf, 0, count - firstCount);
	}

	/// <summary>
	/// Returns an enumerator that iterates through the buffer from oldest to newest.
	/// </summary>
	public Enumerator GetEnumerator() => new Enumerator(this);
	/// <inheritdoc cref="GetEnumerator()"/>
	IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
	/// <inheritdoc cref="GetEnumerator()"/>
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <summary>
	/// Returns an enumerable that enumerates the contents of the buffer from newest to oldest.
	/// </summary>
	public ReverseEnumerable EnumerateReverse() => new ReverseEnumerable(this);

	// ==========================================================================
	// enumerators

	/// <summary>
	/// Enumerates a <see cref="RingBuffer{T}"/> from oldest to newest.
	/// </summary>
	/// <remarks>
	/// Any mutation of the underlying <see cref="RingBuffer{T}"/> causes invalidation.
	/// </remarks>
	public struct Enumerator : IEnumerator<T> {
		private readonly RingBuffer<T> ringbuf;
		private readonly ulong version;
		private int idx;

		internal Enumerator(RingBuffer<T> ringbuf) {
			this.ringbuf = ringbuf;
			version = ringbuf.version;
			idx = -1;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private readonly void throwIfVerChanged() {
			if (version != ringbuf.version)
				throw new InvalidOperationException("collection was modified during enumeration");
		}

		public readonly T Current {
			get {
				throwIfVerChanged();
				if ((uint)idx >= (uint)ringbuf.count)
					throw new InvalidOperationException();
				return ringbuf[idx];
			}
		}

		readonly object? IEnumerator.Current => Current;

		public bool MoveNext() {
			throwIfVerChanged();
			if (idx + 1 >= ringbuf.count)
				return false;
			idx++;
			return true;
		}

		public void Reset() {
			throwIfVerChanged();
			idx = -1;
		}

		public readonly void Dispose() {
		}
	}

	/// <summary>
	/// Enumerable for <see langword="foreach"/> enumeration of a <see cref="RingBuffer{T}"/>
	/// in reverse (newest to oldest) order.
	/// </summary>
	public readonly struct ReverseEnumerable : IEnumerable<T> {
		private readonly RingBuffer<T> ringbuf;

		internal ReverseEnumerable(RingBuffer<T> ringbuf) {
			this.ringbuf = ringbuf;
		}

		public ReverseEnumerator GetEnumerator() => new ReverseEnumerator(ringbuf);
		IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	/// <summary>
	/// Enumerates a <see cref="RingBuffer{T}"/> from newest to oldest.
	/// </summary>
	/// <remarks>
	/// Any mutation of the underlying <see cref="RingBuffer{T}"/> causes invalidation.
	/// </remarks>
	public struct ReverseEnumerator : IEnumerator<T> {
		private readonly RingBuffer<T> ringbuf;
		private readonly ulong version;
		private int idx;

		internal ReverseEnumerator(RingBuffer<T> ringbuf) {
			this.ringbuf = ringbuf;
			version = ringbuf.version;
			idx = ringbuf.count;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private readonly void throwIfVerChanged() {
			if (version != ringbuf.version)
				throw new InvalidOperationException("collection was modified during enumeration");
		}

		public readonly T Current {
			get {
				throwIfVerChanged();
				if ((uint)idx >= (uint)ringbuf.count)
					throw new InvalidOperationException();
				return ringbuf[idx];
			}
		}

		readonly object? IEnumerator.Current => Current;

		public bool MoveNext() {
			throwIfVerChanged();
			if (idx <= 0)
				return false;
			idx--;
			return true;
		}

		public void Reset() {
			throwIfVerChanged();
			idx = ringbuf.count;
		}

		public readonly void Dispose() {
		}
	}

	// ==========================================================================
	// debug view
	private sealed class RingBufferDebugView<TItem>(RingBuffer<TItem> ringbuf) {
		[DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
		public TItem[] Items => ringbuf.ToArray();

		public int Count => ringbuf.Count;
		public int Capacity => ringbuf.Capacity;
	}
}
