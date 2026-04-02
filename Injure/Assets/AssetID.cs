// SPDX-License-Identifier: MIT

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Injure.Assets;

/// <summary>
/// Object representing an asset namespace, used to partition asset IDs.
/// </summary>
public readonly struct AssetNamespace(string value) : IEquatable<AssetNamespace> {
	public readonly string Value = value;

	public bool Equals(AssetNamespace other) => Value == other.Value;
	public override bool Equals(object? obj) => obj is AssetNamespace other && Equals(other);
	public override int GetHashCode() => Value.GetHashCode();
	public static bool operator ==(AssetNamespace left, AssetNamespace right) => left.Equals(right);
	public static bool operator !=(AssetNamespace left, AssetNamespace right) => !left.Equals(right);

	/// <summary>
	/// Returns the namespace string.
	/// </summary>
	public override string ToString() => Value;
}

/// <summary>
/// Unique asset identifier, consisting of a namespace and path.
/// </summary>
[JsonConverter(typeof(AssetIDJsonConverter))]
public readonly struct AssetID(AssetNamespace ns, string path) : IEquatable<AssetID>, ISpanParsable<AssetID> {
	public readonly AssetNamespace Namespace = ns;
	public readonly string Path = path;

	public bool Equals(AssetID other) => Namespace == other.Namespace && Path == other.Path;
	public override bool Equals(object? obj) => obj is AssetID other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Namespace, Path);
	public static bool operator ==(AssetID left, AssetID right) => left.Equals(right);
	public static bool operator !=(AssetID left, AssetID right) => !left.Equals(right);

	/// <summary>
	/// Returns the canonical <c>namespace::path</c> string representation.
	/// </summary>
	public override string ToString() => $"{Namespace}::{Path}";

	/// <summary>
	/// Parses an <see cref="AssetID"/> from the canonical <c>namespace::path</c>
	/// string representation.
	/// </summary>
	/// <param name="s">String to parse.</param>
	/// <param name="val">On success, the parsed value.</param>
	/// <returns>
	/// <see langword="true"/> if the string is in the format <c>namespace::path</c>
	/// and as such the parse succeeded, otherwise <see langword="false"/>.
	/// </returns>
	public static bool TryParse(string? s, out AssetID val) {
		val = default;
		if (s is null)
			return false;
		int i = s.IndexOf("::", StringComparison.Ordinal);
		if (i < 0 || s.IndexOf("::", i + 2, StringComparison.Ordinal) >= 0)
			return false;
		val = new AssetID(new AssetNamespace(s[..i]), s[(i + 2)..]);
		return true;
	}
	/// <inheritdoc cref="TryParse(string, out AssetID)"/>
	/// <remarks>
	/// <paramref name="provider"/> is ignored and is for compatibility with <see cref="IParsable{TSelf}"/>.
	/// </remarks>
	public static bool TryParse(string? s, IFormatProvider? provider, out AssetID val) => TryParse(s, out val);

	/// <summary>
	/// Parses an <see cref="AssetID"/> from the canonical <c>namespace::path</c>
	/// string representation.
	/// </summary>
	/// <param name="s">String to parse.</param>
	/// <returns>
	/// The parsed value.
	/// </returns>
	/// <exception cref="FormatException">
	/// Thrown if the string is not in the format <c>namespace::path</c>.
	/// </exception>
	public static AssetID Parse(string? s) {
		ArgumentNullException.ThrowIfNull(s);
		int i = s.IndexOf("::", StringComparison.Ordinal);
		if (i < 0 || s.IndexOf("::", i + 2, StringComparison.Ordinal) >= 0)
			throw new FormatException("string must contain exactly one occurrence of ::");
		return new AssetID(new AssetNamespace(s[..i]), s[(i + 2)..]);
	}
	/// <inheritdoc cref="Parse(string)"/>
	/// <remarks>
	/// <paramref name="provider"/> is ignored and is for compatibility with <see cref="IParsable{TSelf}"/>.
	/// </remarks>
	public static AssetID Parse(string? s, IFormatProvider? provider) => Parse(s);

	/// <summary>
	/// Parses an <see cref="AssetID"/> from the canonical <c>namespace::path</c>
	/// string representation.
	/// </summary>
	/// <param name="span">Span of characters to parse.</param>
	/// <param name="val">On success, the parsed value.</param>
	/// <returns>
	/// <see langword="true"/> if the string is in the format <c>namespace::path</c>
	/// and as such the parse succeeded, otherwise <see langword="false"/>.
	/// </returns>
	public static bool TryParse(ReadOnlySpan<char> span, out AssetID val) {
		int i = span.IndexOf("::");
		if (i < 0 || span[(i + 2)..].IndexOf("::") >= 0) {
			val = default;
			return false;
		}
		val = new AssetID(new AssetNamespace(new string(span[..i])), new string(span[(i + 2)..]));
		return true;
	}
	/// <inheritdoc cref="TryParse(ReadOnlySpan{char}, out AssetID)"/>
	/// <remarks>
	/// <paramref name="provider"/> is ignored and is for compatibility with <see cref="IParsable{TSelf}"/>.
	/// </remarks>
	public static bool TryParse(ReadOnlySpan<char> span, IFormatProvider? provider, out AssetID val) => TryParse(span, out val);

	/// <summary>
	/// Parses an <see cref="AssetID"/> from the canonical <c>namespace::path</c>
	/// string representation.
	/// </summary>
	/// <param name="span">Span of characters to parse.</param>
	/// <returns>
	/// The parsed value.
	/// </returns>
	/// <exception cref="FormatException">
	/// Thrown if the string is not in the format <c>namespace::path</c>.
	/// </exception>
	public static AssetID Parse(ReadOnlySpan<char> span) {
		int i = span.IndexOf("::");
		if (i < 0 || span[(i + 2)..].IndexOf("::") >= 0)
			throw new FormatException("string must contain exactly one occurrence of ::");
		return new AssetID(new AssetNamespace(new string(span[..i])), new string(span[(i + 2)..]));
	}
	/// <inheritdoc cref="Parse(ReadOnlySpan{char})"/>
	/// <remarks>
	/// <paramref name="provider"/> is ignored and is for compatibility with <see cref="IParsable{TSelf}"/>.
	/// </remarks>
	public static AssetID Parse(ReadOnlySpan<char> span, IFormatProvider? provider) => Parse(span);
}

public sealed class AssetIDJsonConverter : JsonConverter<AssetID> {
	public override AssetID Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => AssetID.Parse(reader.GetString());
	public override void Write(Utf8JsonWriter writer, AssetID val, JsonSerializerOptions options) => writer.WriteStringValue(val.ToString());
}

/// <summary>
/// Typed asset identity used for cycle reporting and internal storage/lookups.
/// </summary>
public readonly record struct AssetKey(AssetID AssetID, Type AssetType);
