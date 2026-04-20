// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;

namespace Injure.Assets;

public sealed class EngineResourceStore {
	// ==========================================================================
	// internal types
	private readonly record struct SourceEntry(IEngineResourceSource Source, int Priority);

	// ==========================================================================
	// internal objects / properties
	private readonly Lock registryLock = new Lock();

	private SourceEntry[] sources = Array.Empty<SourceEntry>();

	// ==========================================================================
	// public api
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

	public bool Exists(EngineResourceID id) => TryGetData(id, out _);

	public bool TryGetData(EngineResourceID id, [NotNullWhen(true)] out EngineResourceData? data) {
		SourceEntry[] snapshot = Volatile.Read(ref sources);
		foreach (SourceEntry ent in snapshot) {
			EngineResourceSourceResult res = ent.Source.TrySource(id);
			switch (res.Kind.Tag) {
			case EngineResourceSourceResultKind.Case.NotHandled:
				continue;
			case EngineResourceSourceResultKind.Case.Success:
				if (res.Data is null)
					throw new EngineResourceException(id, "engine resource source returned Success but didn't set Data");
				data = res.Data;
				return true;
			case EngineResourceSourceResultKind.Case.Error:
				throw res.Exception ?? new EngineResourceException(id, "engine resource source returned Error with no exception attached");
			default:
				throw new UnreachableException();
			}
		}
		data = null;
		return false;
	}

	public EngineResourceData GetData(EngineResourceID id) {
		if (TryGetData(id, out EngineResourceData? data))
			return data;
		throw new EngineResourceException(id, "no registered engine resource source managed to provide the resource");
	}

	public bool TryOpenRead(EngineResourceID id, [NotNullWhen(true)] out Stream? stream) {
		if (!TryGetData(id, out EngineResourceData? data)) {
			stream = null;
			return false;
		}
		stream = data.OpenRead();
		return true;
	}

	public Stream OpenRead(EngineResourceID id) => GetData(id).OpenRead();

	public bool TryGetBytes(EngineResourceID id, [NotNullWhen(true)] out byte[]? data) {
		if (!TryGetData(id, out EngineResourceData? resource)) {
			data = null;
			return false;
		}
		using Stream stream = resource.OpenRead();
		using MemoryStream ms = new MemoryStream();
		stream.CopyTo(ms);
		data = ms.ToArray();
		return true;
	}

	public byte[] GetBytes(EngineResourceID id) {
		if (TryGetBytes(id, out byte[]? data))
			return data;
		throw new EngineResourceException(id, "no registered engine resource source managed to provide the resource");
	}

	public bool TryGetText(EngineResourceID id, [NotNullWhen(true)] out string? text, Encoding? encoding = null) {
		if (!TryGetData(id, out EngineResourceData? resource)) {
			text = null;
			return false;
		}

		using Stream stream = resource.OpenRead();
		using StreamReader reader = new StreamReader(stream, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		text = reader.ReadToEnd();
		return true;
	}

	public string GetText(EngineResourceID id, Encoding? encoding = null) {
		if (TryGetText(id, out string? text, encoding))
			return text;
		throw new EngineResourceException(id, "no registered engine resource source managed to provide the resource");
	}
}
