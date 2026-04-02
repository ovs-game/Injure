// SPDX-License-Identifier: MIT

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Injure;

[JsonConverter(typeof(RectIJsonConverter))]
[StructLayout(LayoutKind.Sequential)]
public readonly struct RectI(int x, int y, int width, int height) : IEquatable<RectI> {
	public readonly int X = x;
	public readonly int Y = y;
	public readonly int Width = width;
	public readonly int Height = height;

	public int Left => X;
	public int Top => Y;
	public int Right => X + Width;
	public int Bottom => Y + Height;

	// these convert to float so i'm not sure if we need them
	//public Vector2 Position => new Vector2(X, Y);
	//public Vector2 Size => new Vector2(Width, Height);
	public bool Contains(Vector2 p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

	public bool Equals(RectI other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
	public override bool Equals(object? obj) => obj is RectI other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
	public static bool operator ==(RectI left, RectI right) => left.Equals(right);
	public static bool operator !=(RectI left, RectI right) => !left.Equals(right);
}

public sealed class RectIJsonConverter : JsonConverter<RectI> {
	public override RectI Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TokenType != JsonTokenType.StartObject)
			throw new JsonException("expected object");
		int? x = null, y = null, width = null, height = null;
		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (x is null || y is null || width is null || height is null)
					throw new JsonException("missing one or more RectI fields");
				return new RectI(x.Value, y.Value, width.Value, height.Value);
			}

			if (reader.TokenType != JsonTokenType.PropertyName)
				throw new JsonException();
			string? name = reader.GetString();
			if (!reader.Read())
				throw new JsonException();

			if (eq(name, "x", options))
				x = reader.GetInt32();
			else if (eq(name, "y", options))
				y = reader.GetInt32();
			else if (eq(name, "width", options))
				width = reader.GetInt32();
			else if (eq(name, "height", options))
				height = reader.GetInt32();
			else
				reader.Skip();
		}
		throw new JsonException("unexpected end of json");
	}

	public override void Write(Utf8JsonWriter writer, RectI val, JsonSerializerOptions options) {
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
