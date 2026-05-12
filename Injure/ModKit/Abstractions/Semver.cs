// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Injure.ModKit.Abstractions;

internal static partial class SemverRegex {
	public const string FullPattern =
		@"^(?<maj>0|[1-9][0-9]*)\.(?<min>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$";

	[GeneratedRegex(FullPattern, RegexOptions.CultureInvariant)]
	public static partial Regex Full();
}

[JsonConverter(typeof(SemverJsonConverter))]
public readonly struct Semver : IEquatable<Semver>, IParsable<Semver> {
	public int Major { get; }
	public int Minor { get; }
	public int Patch { get; }

	public string? Prerelease { get; }
	public string? BuildMetadata { get; }

	public bool IsPrerelease => Prerelease is not null;

	public Semver(int maj, int min, int patch, string? pre = null, string? build = null) {
		ArgumentOutOfRangeException.ThrowIfNegative(maj);
		ArgumentOutOfRangeException.ThrowIfNegative(min);
		ArgumentOutOfRangeException.ThrowIfNegative(patch);
		Major = maj;
		Minor = min;
		Patch = patch;
		Prerelease = validatePre(pre, nameof(pre));
		BuildMetadata = validateBuild(build, nameof(build));
	}

	public bool CompatibleWithMinimum(Semver minimum) {
		if (Major != minimum.Major)
			return false;
		if ((Prerelease is not null || minimum.Prerelease is not null) && (Minor != minimum.Minor || Patch != minimum.Patch))
			return false;
		return cmpIgnoreBuild(this, minimum) >= 0;
	}

	public bool Equals(Semver other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch &&
		string.Equals(Prerelease, other.Prerelease, StringComparison.Ordinal) &&
		string.Equals(BuildMetadata, other.BuildMetadata, StringComparison.Ordinal);
	public override bool Equals(object? obj) => obj is Semver other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease, BuildMetadata);
	public static bool operator ==(Semver left, Semver right) => left.Equals(right);
	public static bool operator !=(Semver left, Semver right) => !left.Equals(right);

	public override string ToString() {
		string s = $"{Major}.{Minor}.{Patch}";
		if (Prerelease is not null)
			s += "-" + Prerelease;
		if (BuildMetadata is not null)
			s += "+" + BuildMetadata;
		return s;
	}

	public static bool TryParse([NotNullWhen(true)] string? s, out Semver val) {
		val = default;
		if (s is null || SemverRegex.Full().Match(s) is not Match { Success: true } m)
			return false;
		if (!int.TryParse(m.Groups["maj"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int maj) ||
			!int.TryParse(m.Groups["min"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int min) ||
			!int.TryParse(m.Groups["patch"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
			return false;
		string? pre = m.Groups["pre"].Success ? m.Groups["pre"].Value : null;
		string? build = m.Groups["build"].Success ? m.Groups["build"].Value : null;
		val = new Semver(maj, min, patch, pre, build);
		return true;
	}
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Semver val) => TryParse(s, out val);

	public static Semver Parse([NotNull] string? s) {
		ArgumentNullException.ThrowIfNull(s);
		if (TryParse(s, out Semver val))
			return val;
		throw new FormatException($"string is not a valid full semver: must match {SemverRegex.FullPattern} and core numeric components must each fit in an int32");
	}
	public static Semver Parse([NotNull] string? s, IFormatProvider? provider) => Parse(s);

	private static string? validatePre(string? value, string paramName) {
		if (value is null)
			return null;
		if (!isValidDottedIdents(value.AsSpan(), prerelease: true))
			throw new ArgumentException("invalid semver prerelease identifiers", paramName);
		return value;
	}
	private static string? validateBuild(string? value, string paramName) {
		if (value is null)
			return null;
		if (!isValidDottedIdents(value.AsSpan(), prerelease: false))
			throw new ArgumentException("invalid semver build metadata identifiers", paramName);
		return value;
	}
	private static bool isValidDottedIdents(ReadOnlySpan<char> s, bool prerelease) {
		if (s.IsEmpty)
			return false;
		int start = 0;
		for (;;) {
			int dot = s[start..].IndexOf('.');
			if (dot < 0)
				return isValidIdent(s[start..], prerelease);
			if (!isValidIdent(s.Slice(start, dot), prerelease))
				return false;
			start += dot + 1;
			if (start >= s.Length)
				return false;
		}
	}
	private static bool isValidIdent(ReadOnlySpan<char> id, bool prerelease) {
		if (id.IsEmpty)
			return false;
		bool allDigits = true;
		foreach (char c in id) {
			bool digit = char.IsAsciiDigit(c);
			if (!digit)
				allDigits = false;
			if (!digit && !char.IsAsciiLetter(c) && c != '-')
				return false;
		}
		return !prerelease || !allDigits || id.Length == 1 || id[0] != '0';
	}

	private static int cmpIgnoreBuild(Semver left, Semver right) {
		int cmp = left.Major.CompareTo(right.Major);
		if (cmp != 0)
			return cmp;
		cmp = left.Minor.CompareTo(right.Minor);
		if (cmp != 0)
			return cmp;
		cmp = left.Patch.CompareTo(right.Patch);
		if (cmp != 0)
			return cmp;
		if (left.Prerelease is null && right.Prerelease is null)
			return 0;
		if (left.Prerelease is null)
			return 1;
		if (right.Prerelease is null)
			return -1;
		return cmpPrerelease(left.Prerelease, right.Prerelease);
	}

	private static int cmpPrerelease(string left, string right) {
		ReadOnlySpan<char> l = left.AsSpan();
		ReadOnlySpan<char> r = right.AsSpan();
		int li = 0;
		int ri = 0;
		while (li < l.Length && ri < r.Length) {
			ReadOnlySpan<char> lid = nextIdent(l, ref li);
			ReadOnlySpan<char> rid = nextIdent(r, ref ri);

			bool lnum = isNumeric(lid);
			bool rnum = isNumeric(rid);

			int cmp;
			if (lnum && rnum)
				cmp = cmpNumericIdent(lid, rid);
			else if (lnum != rnum)
				cmp = lnum ? -1 : 1;
			else
				cmp = cmpAscii(lid, rid);

			if (cmp != 0)
				return cmp;
		}
		if (li == l.Length)
			return ri == r.Length ? 0 : -1;
		return 1;
	}

	private static ReadOnlySpan<char> nextIdent(ReadOnlySpan<char> s, ref int index) {
		int start = index;
		int dot = s[start..].IndexOf('.');
		if (dot < 0) {
			index = s.Length;
			return s[start..];
		}
		index = start + dot + 1;
		return s.Slice(start, dot);
	}

	private static int cmpNumericIdent(ReadOnlySpan<char> left, ReadOnlySpan<char> right) {
		if (left.Length != right.Length)
			return left.Length.CompareTo(right.Length);
		return cmpAscii(left, right);
	}

	private static int cmpAscii(ReadOnlySpan<char> left, ReadOnlySpan<char> right) {
		int len = Math.Min(left.Length, right.Length);
		for (int i = 0; i < len; i++) {
			int cmp = left[i].CompareTo(right[i]);
			if (cmp != 0)
				return cmp;
		}
		return left.Length.CompareTo(right.Length);
	}

	private static bool isNumeric(ReadOnlySpan<char> s) {
		foreach (char c in s)
			if (!char.IsAsciiDigit(c))
				return false;
		return !s.IsEmpty;
	}
}

public sealed class SemverJsonConverter : JsonConverter<Semver> {
	public override Semver Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Semver.Parse(reader.GetString());
	public override void Write(Utf8JsonWriter writer, Semver val, JsonSerializerOptions options) => writer.WriteStringValue(val.ToString());
}
