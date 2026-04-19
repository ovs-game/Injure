// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Injure.Assets;

/// <summary>
/// Unique asset identifier, consisting of a namespace and path.
/// </summary>
[JsonConverter(typeof(AssetIDJsonConverter))]
public readonly struct AssetID : IEquatable<AssetID>, ISpanParsable<AssetID> {
	public readonly string Namespace;
	public readonly string Path;

	public AssetID(string ns, string path) : this(ns, path, skipValidation: false) {
	}

	internal AssetID(string ns, string path, bool skipValidation) {
		if (!skipValidation) {
			if (!tryValidateNamespace(ns, out string? err))
				throw new ArgumentException(err, nameof(ns));
			if (!tryValidatePath(path, out err))
				throw new ArgumentException(err, nameof(path));
		}
		Namespace = ns;
		Path = path;
	}

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
	/// and has a valid namespace and path, and as such the parse succeeded,
	/// otherwise <see langword="false"/>.
	/// </returns>
	public static bool TryParse([NotNullWhen(true)] string? s, out AssetID val) {
		val = default;
		if (s is null)
			return false;
		int i = s.IndexOf("::", StringComparison.Ordinal);
		if (i < 0 || s.IndexOf("::", i + 2, StringComparison.Ordinal) >= 0)
			return false;
		if (!tryValidateNamespace(s.AsSpan(0, i), out _) || !tryValidatePath(s.AsSpan(i + 2), out _))
			return false;
		val = new AssetID(s[..i], s[(i + 2)..], skipValidation: true);
		return true;
	}
	/// <inheritdoc cref="TryParse(string, out AssetID)"/>
	/// <remarks>
	/// <paramref name="provider"/> is ignored and is for compatibility with <see cref="IParsable{TSelf}"/>.
	/// </remarks>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AssetID val) => TryParse(s, out val);

	/// <summary>
	/// Parses an <see cref="AssetID"/> from the canonical <c>namespace::path</c>
	/// string representation.
	/// </summary>
	/// <param name="s">String to parse.</param>
	/// <returns>
	/// The parsed value.
	/// </returns>
	/// <exception cref="FormatException">
	/// Thrown if the string is not in the format <c>namespace::path</c> or has
	/// an invalid namespace or path.
	/// </exception>
	public static AssetID Parse([NotNull] string? s) {
		ArgumentNullException.ThrowIfNull(s);
		int i = s.IndexOf("::", StringComparison.Ordinal);
		if (i < 0 || s.IndexOf("::", i + 2, StringComparison.Ordinal) >= 0)
			throw new FormatException("string must contain exactly one occurrence of ::");
		validateNamespace(s.AsSpan(0, i));
		validatePath(s.AsSpan(i + 2));
		return new AssetID(s[..i], s[(i + 2)..], skipValidation: true);
	}
	/// <inheritdoc cref="Parse(string)"/>
	/// <remarks>
	/// <paramref name="provider"/> is ignored and is for compatibility with <see cref="IParsable{TSelf}"/>.
	/// </remarks>
	public static AssetID Parse([NotNull] string? s, IFormatProvider? provider) => Parse(s);

	/// <summary>
	/// Parses an <see cref="AssetID"/> from the canonical <c>namespace::path</c>
	/// string representation.
	/// </summary>
	/// <param name="span">Span of characters to parse.</param>
	/// <param name="val">On success, the parsed value.</param>
	/// <returns>
	/// <see langword="true"/> if the string is in the format <c>namespace::path</c>
	/// and has a valid namespace and path, and as such the parse succeeded,
	/// otherwise <see langword="false"/>.
	/// </returns>
	public static bool TryParse(ReadOnlySpan<char> span, out AssetID val) {
		val = default;
		int i = span.IndexOf("::");
		if (i < 0 || span[(i + 2)..].IndexOf("::") >= 0)
			return false;
		if (!tryValidateNamespace(span[..i], out _) || !tryValidatePath(span[(i + 2)..], out _))
			return false;
		val = new AssetID(new string(span[..i]), new string(span[(i + 2)..]), skipValidation: true);
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
	/// Thrown if the string is not in the format <c>namespace::path</c> or has
	/// an invalid namespace or path.
	/// </exception>
	public static AssetID Parse(ReadOnlySpan<char> span) {
		int i = span.IndexOf("::");
		if (i < 0 || span[(i + 2)..].IndexOf("::") >= 0)
			throw new FormatException("string must contain exactly one occurrence of ::");
		validateNamespace(span[..i]);
		validatePath(span[(i + 2)..]);
		return new AssetID(new string(span[..i]), new string(span[(i + 2)..]), skipValidation: true);
	}
	/// <inheritdoc cref="Parse(ReadOnlySpan{char})"/>
	/// <remarks>
	/// <paramref name="provider"/> is ignored and is for compatibility with <see cref="IParsable{TSelf}"/>.
	/// </remarks>
	public static AssetID Parse(ReadOnlySpan<char> span, IFormatProvider? provider) => Parse(span);

	// ==========================================================================
	// the string validation corner
	private static bool tryValidateNamespace(ReadOnlySpan<char> s, [NotNullWhen(false)] out string? err) {
		if (s.IsEmpty) {
			err = "asset namespace must not be empty";
			return false;
		}
		if (!char.IsAsciiLetterOrDigit(s[0])) {
			err = "asset namespace must start with an ASCII letter or ASCII digit";
			return false;
		}
		foreach (char c in s) {
			if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '.')) {
				err = $"asset namespace contains invalid UTF-16 code unit U+{(ushort)c:X4} '{c}' (valid: ASCII letters, ASCII digits, '_', '-', '.')";
				return false;
			}
		}
		err = null;
		return true;
	}
	private static void validateNamespace(ReadOnlySpan<char> s) {
		if (!tryValidateNamespace(s, out string? err))
			throw new FormatException(err);
	}

	private static bool tryValidatePath(ReadOnlySpan<char> s, [NotNullWhen(false)] out string? err) {
		static bool checkSeg(ReadOnlySpan<char> seg, [NotNullWhen(false)] out string? err) {
			if (seg.IsEmpty) {
				err = "asset path must not contain empty path segments";
				return false;
			}
			if (seg.SequenceEqual(".")) {
				err = "asset path must not contain '.' path segments";
				return false;
			}
			if (seg.SequenceEqual("..")) {
				err = "asset path must not contain '..' path segments";
				return false;
			}
			err = null;
			return true;
		}

		if (s.IsEmpty) {
			err = "asset path must not be empty";
			return false;
		}
		if (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1])) {
			err = "asset path must not have leading/trailing whitespace";
			return false;
		}
		if (s[0] == '/') {
			err = "asset path must not start with '/' (rooted to... what?)";
			return false;
		}
		if (s[^1] == '/') {
			err = "asset path must not end with '/'";
			return false;
		}
		int segStart = 0;
		for (int i = 0; i < s.Length; i++) {
			char c = s[i];
			if (char.IsControl(c)) {
				err = "asset path must not contain control characters";
				return false;
			}
			if (c == '\\') {
				err = "asset path must use '/' as the path separator, not '\\'";
				return false;
			}
			if (c == '/') {
				ReadOnlySpan<char> seg = s[segStart..i];
				if (!checkSeg(seg, out err))
					return false;
				segStart = i + 1;
			}
		}
		ReadOnlySpan<char> lastSeg = s[segStart..];
		if (!checkSeg(lastSeg, out err))
			return false;
		err = null;
		return true;
	}
	private static void validatePath(ReadOnlySpan<char> s) {
		if (!tryValidatePath(s, out string? err))
			throw new FormatException(err);
	}
}

public sealed class AssetIDJsonConverter : JsonConverter<AssetID> {
	public override AssetID Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => AssetID.Parse(reader.GetString());
	public override void Write(Utf8JsonWriter writer, AssetID val, JsonSerializerOptions options) => writer.WriteStringValue(val.ToString());
}

/// <summary>
/// Typed asset identity used for cycle reporting and internal storage/lookups.
/// </summary>
public readonly record struct AssetKey(AssetID AssetID, Type AssetType);
