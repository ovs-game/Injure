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

internal readonly record struct LayerTagNamespaceID(ulong ID);
internal readonly record struct TagKey(LayerTagNamespaceID NamespaceID, string Name);

public struct LayerTagSet {
	private LayerTag[]? items;
	private int count;
	private const int startingCount = 4;

	public LayerTagSet(ReadOnlySpan<LayerTag> tags) {
		for (int i = 0; i < tags.Length; i++)
			Add(tags[i]);
	}

	public readonly int Count => count;

	public readonly bool Contains(LayerTag tag) {
		LayerTag[]? arr = items;
		if (arr is null)
			return false;
		for (int i = 0; i < count; i++)
			if (arr[i].ID == tag.ID)
				return true;
		return false;
	}

	public void Add(LayerTag tag) {
		if (Contains(tag))
			return;
		if (items is null)
			items = new LayerTag[startingCount];
		else if (items.Length < count)
			Array.Resize(ref items, items.Length * 2);
		items[count++] = tag;
	}

	public readonly bool Intersects(in LayerTagSet other) {
		for (int i = 0; i < count; i++)
			if (other.Contains(items![i]))
				return true;
		return false;
	}

	public readonly ReadOnlySpan<LayerTag> AsSpan() => items is null ? ReadOnlySpan<LayerTag>.Empty : items.AsSpan(0, count);
}

public sealed class LayerTagRegistry {
	private readonly TwoWayMap<string, LayerTagNamespaceID> namespaces;
	private readonly TwoWayMap<TagKey, LayerTag> tags;

	// first will be 1 since these get incremented upfront
	private ulong nextNamespaceID = 0;
	private ulong nextTagID = 0;

	internal LayerTagRegistry() {
		namespaces = new TwoWayMap<string, LayerTagNamespaceID>(cmpLeft: StringComparer.Ordinal);
		tags = new TwoWayMap<TagKey, LayerTag>();
	}

	public LayerTag GetOrCreate(string ns, string name) {
		validate(ns, nameof(ns), "layer tag namespace");
		validate(name, nameof(name), "layer tag name");
		LayerTagNamespaceID nsID = getOrCreateNs(ns);
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

	private LayerTagNamespaceID getOrCreateNs(string ns) {
		if (namespaces.TryGetByLeft(ns, out LayerTagNamespaceID id))
			return id;
		id = new LayerTagNamespaceID(++nextNamespaceID);
		namespaces.Add(ns, id);
		return id;
	}

	private static void validate(ReadOnlySpan<char> s, string paramName, string kind) {
		if (s.IsEmpty)
			throw new ArgumentException(kind + " must not be empty", paramName);
		if (!char.IsAsciiLetterOrDigit(s[0]))
			throw new ArgumentException(kind + " must start with an ASCII letter or ASCII digit", paramName);
		for (int i = 0; i < s.Length; i++) {
			char c = s[i];
			if (char.IsControl(c))
				throw new ArgumentException(kind + " must not contain control characters", paramName);
			if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '.'))
				throw new ArgumentException($"{kind} contains invalid character '{c} (valid: ASCII letters, ASCII digits, '_', '-', '.')", paramName);
		}
	}
}
