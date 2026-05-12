// SPDX-License-Identifier: MIT

using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Injure.ModKit.Abstractions;

public sealed class ModLiveStateBlob {
	private static readonly UTF8Encoding strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	private readonly byte[] data;

	public Semver SchemaVersion { get; }
	public string ContentType { get; }

	public int Length => data.Length;
	public ReadOnlyMemory<byte> Data => data;
	public ReadOnlySpan<byte> Span => data;

	private ModLiveStateBlob(Semver schemaVersion, string contentType, byte[] data) {
		validateContentType(contentType);
		SchemaVersion = schemaVersion;
		ContentType = contentType;
		this.data = data;
	}

	public static ModLiveStateBlob FromBytes(Semver schemaVersion, ReadOnlySpan<byte> bytes,
		string contentType = ModLiveStateContentTypes.ApplicationOctetStream) =>
		new(schemaVersion, contentType, bytes.ToArray());

	public static ModLiveStateBlob TakeBytes(Semver schemaVersion, byte[] bytes,
		string contentType = ModLiveStateContentTypes.ApplicationOctetStream) {
		ArgumentNullException.ThrowIfNull(bytes);
		return new ModLiveStateBlob(schemaVersion, contentType, bytes);
	}

	public static ModLiveStateBlob FromUtf8(Semver schemaVersion, string text,
		string contentType = ModLiveStateContentTypes.TextPlainUtf8) {
		ArgumentNullException.ThrowIfNull(text);
		return TakeBytes(schemaVersion, strictUtf8.GetBytes(text), contentType);
	}

	public static ModLiveStateBlob FromJson<T>(Semver schemaVersion, T value,
		JsonTypeInfo<T> jsonTypeInfo, string contentType = ModLiveStateContentTypes.ApplicationJson) {
		ArgumentNullException.ThrowIfNull(jsonTypeInfo);
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo);
		return TakeBytes(schemaVersion, bytes, contentType);
	}

	public byte[] ToArray() => data.ToArray();

	public Stream OpenRead() => new MemoryStream(data, writable: false);

	public string ReadUtf8(string requiredMediaType = ModLiveStateContentTypes.TextPlainUtf8) {
		RequireMediaType(requiredMediaType);
		return strictUtf8.GetString(data);
	}

	public T? ReadJson<T>(JsonTypeInfo<T> jsonTypeInfo, string requiredMediaType = ModLiveStateContentTypes.ApplicationJson) {
		ArgumentNullException.ThrowIfNull(jsonTypeInfo);
		RequireMediaType(requiredMediaType);
		return JsonSerializer.Deserialize(data, jsonTypeInfo);
	}

	public T ReadRequiredJson<T>(JsonTypeInfo<T> jsonTypeInfo, string requiredMediaType = ModLiveStateContentTypes.ApplicationJson) where T : notnull =>
		ReadJson(jsonTypeInfo, requiredMediaType) ?? throw new ModLiveStateFormatException("JSON deserialized to null");

	public bool HasMediaType(string mediaType) {
		validateContentType(mediaType);
		return mediaTypeSpan(ContentType).Equals(mediaTypeSpan(mediaType), StringComparison.OrdinalIgnoreCase);
	}

	public void RequireMediaType(string mediaType) {
		if (!HasMediaType(mediaType))
			throw new ModLiveStateFormatException($"live state has content type '{ContentType}', expected '{mediaType}'");
	}

	private static ReadOnlySpan<char> mediaTypeSpan(string contentType) {
		int semicolon = contentType.IndexOf(';');
		ReadOnlySpan<char> span = semicolon >= 0 ? contentType.AsSpan(0, semicolon) : contentType.AsSpan();
		return span.Trim();
	}

	private static void validateContentType(string contentType) {
		ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
		foreach (char c in contentType)
			if (c is '\r' or '\n' or '\0')
				throw new ArgumentException("content type must not contain CR, LF, or NUL", nameof(contentType));
	}
}

public static class ModLiveStateContentTypes {
	public const string ApplicationOctetStream = "application/octet-stream";
	public const string ApplicationJson = "application/json";
	public const string TextPlainUtf8 = "text/plain; charset=utf-8";
}

public sealed class ModLiveStateFormatException(string message) : Exception(message) {
}
