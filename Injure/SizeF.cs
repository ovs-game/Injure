// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Injure;

[JsonConverter(typeof(SizeFJsonConverter))]
[StructLayout(LayoutKind.Sequential)]
public readonly struct SizeF(float width, float height) : IEquatable<SizeF> {
	public readonly float Width = !float.IsNaN(width) && width >= 0f ? width : throw new ArgumentOutOfRangeException(nameof(width), width, "width cannot be NaN or negative");
	public readonly float Height = !float.IsNaN(height) && height >= 0f ? height : throw new ArgumentOutOfRangeException(nameof(height), height, "height cannot be NaN or negative");

	public static readonly SizeF Zero = new(0f, 0f);

	public bool Equals(SizeF other) => Width == other.Width && Height == other.Height;
	public override bool Equals(object? obj) => obj is SizeF other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Width, Height);
	public static bool operator ==(SizeF left, SizeF right) => left.Equals(right);
	public static bool operator !=(SizeF left, SizeF right) => !left.Equals(right);
}

public sealed class SizeFJsonConverter : JsonConverter<SizeF> {
	public override SizeF Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TokenType != JsonTokenType.StartObject)
			throw new JsonException("expected object");
		float? width = null, height = null;
		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (width is null || height is null)
					throw new JsonException("missing one or more SizeF fields");
				return new SizeF(width.Value, height.Value);
			}

			if (reader.TokenType != JsonTokenType.PropertyName)
				throw new JsonException();
			string? name = reader.GetString();
			if (!reader.Read())
				throw new JsonException();

			if (eq(name, "width", options))
				width = reader.GetSingle();
			else if (eq(name, "height", options))
				height = reader.GetSingle();
			else
				reader.Skip();
		}
		throw new JsonException("unexpected end of json");
	}

	public override void Write(Utf8JsonWriter writer, SizeF val, JsonSerializerOptions options) {
		writer.WriteStartObject();
		writer.WriteNumber("width", val.Width);
		writer.WriteNumber("height", val.Height);
		writer.WriteEndObject();
	}

	private static bool eq(string? got, string expected, JsonSerializerOptions options) =>
		string.Equals(got, expected, options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
