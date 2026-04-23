// SPDX-License-Identifier: MIT

using System;

using Injure.DataStructures;

namespace Injure.Layers;

public readonly struct LayerTag : IEquatable<LayerTag> {
	internal readonly ulong ID; // IDs start at 1 so `default` isn't a valid tag
	internal LayerTag(ulong id) {
		ID = id;
	}

	public bool Equals(LayerTag other) => ID == other.ID;
	public override bool Equals(object? obj) => obj is LayerTag other && Equals(other);
	public override int GetHashCode() => ID.GetHashCode();
	public static bool operator ==(LayerTag left, LayerTag right) => left.Equals(right);
	public static bool operator !=(LayerTag left, LayerTag right) => !left.Equals(right);
}

internal readonly record struct TagKey(ulong NamespaceID, string Name);

public readonly struct LayerTagSet {
	private readonly LayerTag[]? items;

	public static readonly LayerTagSet Empty = default;

	public LayerTagSet(ReadOnlySpan<LayerTag> tags) {
		if (tags.IsEmpty) {
			items = null;
			return;
		}

		LayerTag[] arr = tags.ToArray();
		int count = 0;
		for (int i = 0; i < arr.Length; i++) {
			LayerTag tag = arr[i];
			bool seen = false;
			for (int j = 0; j < count; j++) {
				if (arr[j] == tag) {
					seen = true;
					break;
				}
			}
			if (!seen)
				arr[count++] = tag;
		}

		if (count == 0) {
			items = null;
		} else if (count == arr.Length) {
			items = arr;
		} else {
			items = arr[..count];
		}
	}

	public int Count => items?.Length ?? 0;

	public bool Contains(LayerTag tag) {
		LayerTag[]? arr = items;
		if (arr is null)
			return false;
		for (int i = 0; i < arr.Length; i++)
			if (arr[i] == tag)
				return true;
		return false;
	}

	public bool Intersects(ReadOnlySpan<LayerTag> other) {
		LayerTag[]? arr = items;
		if (arr is null || other.IsEmpty)
			return false;
		for (int i = 0; i < other.Length; i++)
			if (Contains(other[i]))
				return true;
		return false;
	}

	public bool Intersects(in LayerTagSet other) => Intersects(other.AsSpan());

	public ReadOnlySpan<LayerTag> AsSpan() => items is null ? ReadOnlySpan<LayerTag>.Empty : items;
}

public sealed class LayerTagRegistry {
	private readonly TwoWayMap<string, ulong> namespaces;
	private readonly TwoWayMap<TagKey, LayerTag> tags;

	// first will be 1 since these get incremented upfront
	private ulong nextNamespaceID = 0;
	private ulong nextTagID = 0;

	internal LayerTagRegistry() {
		namespaces = new TwoWayMap<string, ulong>(cmpLeft: StringComparer.Ordinal);
		tags = new TwoWayMap<TagKey, LayerTag>();
	}

	public LayerTag GetOrCreate(string ns, string name) {
		validate(ns, nameof(ns), "layer tag namespace");
		validate(name, nameof(name), "layer tag name");
		ulong nsID = getOrCreateNs(ns);
		TagKey key = new(nsID, name);
		if (tags.TryGetByLeft(key, out LayerTag tag))
			return tag;
		tag = new LayerTag(++nextTagID);
		tags.Add(key, tag);
		return tag;
	}

	public string GetQualifiedName(LayerTag tag) {
		if (!tags.TryGetByRight(tag, out TagKey key))
			throw new ArgumentException("unknown layer tag", nameof(tag));
		if (!namespaces.TryGetByRight(key.NamespaceID, out string? ns))
			throw new ArgumentException("unknown layer tag", nameof(tag));
		return ns + "::" + key.Name;
	}

	private ulong getOrCreateNs(string ns) {
		if (namespaces.TryGetByLeft(ns, out ulong id))
			return id;
		id = ++nextNamespaceID;
		namespaces.Add(ns, id);
		return id;
	}

	private static void validate(ReadOnlySpan<char> s, string paramName, string kind) {
		if (s.IsEmpty)
			throw new ArgumentException(kind + " must not be empty", paramName);
		if (!char.IsAsciiLetterOrDigit(s[0]))
			throw new ArgumentException(kind + " must start with an ASCII letter or ASCII digit", paramName);
		foreach (char c in s)
			if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '.'))
				throw new ArgumentException($"{kind} contains invalid UTF-16 code unit U+{(ushort)c:X4} '{c}' (valid: ASCII letters, ASCII digits, '_', '-', '.')", paramName);
	}
}
