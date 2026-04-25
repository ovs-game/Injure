// SPDX-License-Identifier: MIT

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Injure;

[JsonConverter(typeof(Vector2IntJsonConverter))]
[StructLayout(LayoutKind.Sequential)]
public readonly struct Vector2Int(int x, int y) : IEquatable<Vector2Int> {
	public readonly int X = x;
	public readonly int Y = y;

	public Vector2 ToVector2() => new(X, Y);

	public bool Equals(Vector2Int other) => X == other.X && Y == other.Y;
	public override bool Equals(object? obj) => obj is Vector2Int other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(X, Y);
	public static bool operator ==(Vector2Int left, Vector2Int right) => left.Equals(right);
	public static bool operator !=(Vector2Int left, Vector2Int right) => !left.Equals(right);
}

public sealed class Vector2IntJsonConverter : JsonConverter<Vector2Int> {
	public override Vector2Int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TokenType != JsonTokenType.StartObject)
			throw new JsonException("expected object");
		int? x = null, y = null;
		while (reader.Read()) {
			if (reader.TokenType == JsonTokenType.EndObject) {
				if (x is null || y is null)
					throw new JsonException("missing one or more Vector2Int fields");
				return new Vector2Int(x.Value, y.Value);
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
			else
				reader.Skip();
		}
		throw new JsonException("unexpected end of json");
	}

	public override void Write(Utf8JsonWriter writer, Vector2Int val, JsonSerializerOptions options) {
		writer.WriteStartObject();
		writer.WriteNumber("x", val.X);
		writer.WriteNumber("y", val.Y);
		writer.WriteEndObject();
	}

	private static bool eq(string? got, string expected, JsonSerializerOptions options) =>
		string.Equals(got, expected, options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
