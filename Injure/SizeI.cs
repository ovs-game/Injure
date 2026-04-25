// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Injure;

[JsonConverter(typeof(SizeIJsonConverter))]
[StructLayout(LayoutKind.Sequential)]
public readonly struct SizeI(int width, int height) : IEquatable<SizeI> {
	public readonly int Width = width >= 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
	public readonly int Height = height >= 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));

	public static readonly SizeI Zero = new(0, 0);

	public bool Equals(SizeI other) => Width == other.Width && Height == other.Height;
	public override bool Equals(object? obj) => obj is SizeI other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Width, Height);
	public static bool operator ==(SizeI left, SizeI right) => left.Equals(right);
	public static bool operator !=(SizeI left, SizeI right) => !left.Equals(right);
}

public sealed class SizeIJsonConverter : JsonConverter<SizeI> {
	public override SizeI Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TokenType != JsonTokenType.StartObject)
			throw new JsonException("expected object");
		int? width = null, height = null;
		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (width is null || height is null)
					throw new JsonException("missing one or more SizeI fields");
				return new SizeI(width.Value, height.Value);
			}

			if (reader.TokenType != JsonTokenType.PropertyName)
				throw new JsonException();
			string? name = reader.GetString();
			if (!reader.Read())
				throw new JsonException();

			if (eq(name, "width", options))
				width = reader.GetInt32();
			else if (eq(name, "height", options))
				height = reader.GetInt32();
			else
				reader.Skip();
		}
		throw new JsonException("unexpected end of json");
	}

	public override void Write(Utf8JsonWriter writer, SizeI val, JsonSerializerOptions options) {
		writer.WriteStartObject();
		writer.WriteNumber("width", val.Width);
		writer.WriteNumber("height", val.Height);
		writer.WriteEndObject();
	}

	private static bool eq(string? got, string expected, JsonSerializerOptions options) =>
		string.Equals(got, expected, options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
