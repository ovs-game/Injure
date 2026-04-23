// SPDX-License-Identifier: MIT

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Injure;

[JsonConverter(typeof(RectFJsonConverter))]
[StructLayout(LayoutKind.Sequential)]
public readonly struct RectF(float x, float y, float width, float height) : IEquatable<RectF> {
	public readonly float X = x;
	public readonly float Y = y;
	public readonly float Width = width;
	public readonly float Height = height;

	public float Left => X;
	public float Top => Y;
	public float Right => X + Width;
	public float Bottom => Y + Height;

	public Vector2 Position => new(X, Y);
	public Vector2 Size => new(Width, Height);
	public bool Contains(Vector2 p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

	public bool Equals(RectF other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
	public override bool Equals(object? obj) => obj is RectF other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
	public static bool operator ==(RectF left, RectF right) => left.Equals(right);
	public static bool operator !=(RectF left, RectF right) => !left.Equals(right);
}

public sealed class RectFJsonConverter : JsonConverter<RectF> {
	public override RectF Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TokenType != JsonTokenType.StartObject)
			throw new JsonException("expected object");
		float? x = null, y = null, width = null, height = null;
		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (x is null || y is null || width is null || height is null)
					throw new JsonException("missing one or more RectF fields");
				return new RectF(x.Value, y.Value, width.Value, height.Value);
			}

			if (reader.TokenType != JsonTokenType.PropertyName)
				throw new JsonException();
			string? name = reader.GetString();
			if (!reader.Read())
				throw new JsonException();

			if (eq(name, "x", options))
				x = reader.GetSingle();
			else if (eq(name, "y", options))
				y = reader.GetSingle();
			else if (eq(name, "width", options))
				width = reader.GetSingle();
			else if (eq(name, "height", options))
				height = reader.GetSingle();
			else
				reader.Skip();
		}
		throw new JsonException("unexpected end of json");
	}

	public override void Write(Utf8JsonWriter writer, RectF val, JsonSerializerOptions options) {
		writer.WriteStartObject();
		writer.WriteNumber("x", val.X);
		writer.WriteNumber("y", val.Y);
		writer.WriteNumber("width", val.Width);
		writer.WriteNumber("height", val.Height);
		writer.WriteEndObject();
	}

	private static bool eq(string? got, string expected, JsonSerializerOptions options) =>
		string.Equals(got, expected, options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
