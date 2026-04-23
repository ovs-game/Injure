// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;

namespace Injure.Assets;

/// <summary>
/// Lightweight resource store for mandatory engine resources.
/// </summary>
/// <remarks>
/// Intended for simple always-available resources such as built-in engine data: no
/// hot reload, no dependency tracking, no async, no multi-step pipeline, and resources
/// are treated as plain byte blobs.
/// </remarks>
public sealed class EngineResourceStore {
	// ==========================================================================
	// internal types
	private readonly record struct SourceEntry(IEngineResourceSource Source, int Priority);

	// ==========================================================================
	// internal objects / properties
	private readonly Lock registryLock = new();
	private SourceEntry[] sources = Array.Empty<SourceEntry>();

	// ==========================================================================
	// public api

	/// <summary>
	/// Registers an engine resource source.
	/// </summary>
	/// <param name="source">Source to register.</param>
	/// <param name="priority">Source priority; sources with higher priority values are tried first.</param>
	/// <remarks>
	/// <para>Priority ordering is temporary and is expected to be replaced by owner-ordering soon.</para>
	/// <para>Unregistry is not supported, though this decision is not final.</para>
	/// </remarks>
	public void RegisterSource(IEngineResourceSource source, int priority = 0) {
		ArgumentNullException.ThrowIfNull(source);
		lock (registryLock) {
			SourceEntry[] old = sources;
			SourceEntry[] @new = new SourceEntry[old.Length + 1];
			Array.Copy(old, @new, old.Length);
			@new[old.Length] = new SourceEntry(source, priority);
			Array.Sort(@new, static (SourceEntry a, SourceEntry b) => b.Priority.CompareTo(a.Priority));
			Volatile.Write(ref sources, @new);
		}
	}

	/// <summary>
	/// Queries every source for an engine resource and returns <see langword="true"/>
	/// if any of them provided it.
	/// </summary>
	/// <param name="id">Resource ID.</param>
	public bool Exists(EngineResourceID id) => TryGetData(id, out _);

	/// <summary>
	/// Attempts to get metadata/opening data for an engine resource.
	/// </summary>
	/// <param name="id">Resource ID.</param>
	/// <param name="data">On success, resource data.</param>
	/// <returns><see langword="true"/> if a source provided the resource; otherwise, <see langword="false"/>.</returns>
	public bool TryGetData(EngineResourceID id, [NotNullWhen(true)] out EngineResourceData? data) {
		SourceEntry[] snapshot = Volatile.Read(ref sources);
		foreach (SourceEntry ent in snapshot) {
			EngineResourceSourceResult res = ent.Source.TryCreate(id);
			switch (res.Kind.Tag) {
			case EngineResourceSourceResultKind.Case.NotHandled:
				continue;
			case EngineResourceSourceResultKind.Case.Success:
				data = res.Data ?? throw new EngineResourceException(id, "engine resource source returned Success but didn't set Data");
				return true;
			default:
				throw new UnreachableException();
			}
		}
		data = null;
		return false;
	}

	/// <summary>
	/// Gets metadata/opening data for an engine resource.
	/// </summary>
	/// <param name="id">Resource ID.</param>
	/// <exception cref="EngineResourceException">Thrown if no source managed to provide the resource.</exception>
	public EngineResourceData GetData(EngineResourceID id) {
		if (TryGetData(id, out EngineResourceData? data))
			return data;
		throw new EngineResourceException(id, "no registered engine resource source managed to provide the resource");
	}

	/// <summary>
	/// Opens an engine resource for reading if it exists.
	/// </summary>
	/// <param name="id">Resource ID.</param>
	/// <param name="stream">On success, a fresh readable stream owned by the caller.</param>
	/// <returns><see langword="true"/> if a source provided the resource; otherwise, <see langword="false"/>.</returns>
	public bool TryOpenRead(EngineResourceID id, [NotNullWhen(true)] out Stream? stream) {
		if (!TryGetData(id, out EngineResourceData? data)) {
			stream = null;
			return false;
		}
		stream = data.OpenRead();
		return true;
	}

	/// <summary>
	/// Opens an engine resource for reading.
	/// </summary>
	/// <param name="id">Resource ID.</param>
	/// <exception cref="EngineResourceException">Thrown if no source managed to provide the resource.</exception>
	public Stream OpenRead(EngineResourceID id) => GetData(id).OpenRead();

	/// <summary>
	/// Reads an entire engine resource as bytes if it exists.
	/// </summary>
	/// <param name="id">Resource ID.</param>
	/// <param name="data">On success, the read data.</param>
	/// <returns><see langword="true"/> if a source provided the resource; otherwise, <see langword="false"/>.</returns>
	public bool TryGetBytes(EngineResourceID id, [NotNullWhen(true)] out byte[]? data) {
		if (!TryGetData(id, out EngineResourceData? resource)) {
			data = null;
			return false;
		}
		using Stream stream = resource.OpenRead();
		using MemoryStream ms = new();
		stream.CopyTo(ms);
		data = ms.ToArray();
		return true;
	}

	/// <summary>
	/// Reads an entire engine resource as bytes.
	/// </summary>
	/// <param name="id">Resource ID.</param>
	/// <exception cref="EngineResourceException">Thrown if no source managed to provide the resource.</exception>
	public byte[] GetBytes(EngineResourceID id) {
		if (TryGetBytes(id, out byte[]? data))
			return data;
		throw new EngineResourceException(id, "no registered engine resource source managed to provide the resource");
	}

	/// <summary>
	/// Reads an entire engine resource as text if it exists.
	/// </summary>
	/// <param name="id">Resource ID.</param>
	/// <param name="text">On success, the read text.</param>
	/// <param name="encoding">Encoding to use, or <see langword="null"/> for UTF-8.</param>
	/// <returns><see langword="true"/> if a source provided the resource; otherwise, <see langword="false"/>.</returns>
	public bool TryGetText(EngineResourceID id, [NotNullWhen(true)] out string? text, Encoding? encoding = null) {
		if (!TryGetData(id, out EngineResourceData? resource)) {
			text = null;
			return false;
		}

		using Stream stream = resource.OpenRead();
		using StreamReader reader = new(stream, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		text = reader.ReadToEnd();
		return true;
	}

	/// <summary>
	/// Reads an entire engine resource as text.
	/// </summary>
	/// <param name="id">Resource ID.</param>
	/// <param name="encoding">Encoding to use, or <see langword="null"/> for UTF-8.</param>
	/// <exception cref="EngineResourceException">Thrown if no source managed to provide the resource.</exception>
	public string GetText(EngineResourceID id, Encoding? encoding = null) {
		if (TryGetText(id, out string? text, encoding))
			return text;
		throw new EngineResourceException(id, "no registered engine resource source managed to provide the resource");
	}
}
