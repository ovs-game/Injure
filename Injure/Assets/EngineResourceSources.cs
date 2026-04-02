// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Injure.Assets;

public sealed class DirectoryEngineResourceSource(string root) : IEngineResourceSource {
	private readonly string root = !string.IsNullOrWhiteSpace(root) ? root : throw new ArgumentException("root must be non-null/empty/whitespace");

	public EngineResourceSourceResult TrySource(EngineResourceID id) {
		string path = Path.Combine(root, id.Path.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(path))
			return EngineResourceSourceResult.NotHandled();

		return EngineResourceSourceResult.Success(new EngineResourceData(
			openRead: () => File.OpenRead(path),
			debugName: path,
			suggestedExtension: Path.GetExtension(path),
			origin: path
		));
	}
}

public sealed class EmbeddedEngineResourceSource : IEngineResourceSource {
	private readonly Assembly assembly;
	private readonly HashSet<EngineResourceID> ids;

	public EmbeddedEngineResourceSource(Assembly assembly, HashSet<EngineResourceID> ids) {
		ArgumentNullException.ThrowIfNull(assembly);
		ArgumentNullException.ThrowIfNull(ids);
		this.assembly = assembly;
		this.ids = ids;
	}

	public EngineResourceSourceResult TrySource(EngineResourceID id) {
		if (!ids.Contains(id))
			return EngineResourceSourceResult.NotHandled();

		try {
			Stream? stream = assembly.GetManifestResourceStream(id.Path);
			if (stream is null)
				return EngineResourceSourceResult.Error(new FileNotFoundException("failed to open embedded engine resource stream", id.Path));
			stream.Dispose();
			return EngineResourceSourceResult.Success(new EngineResourceData(
				openRead: () => assembly.GetManifestResourceStream(id.Path) ?? throw new FileNotFoundException("failed to open embedded engine resource stream", id.Path),
				debugName: id.Path,
				suggestedExtension: Path.GetExtension(id.Path),
				origin: assembly
			));
		} catch (Exception ex) {
			return EngineResourceSourceResult.Error(ex);
		}
	}
}
