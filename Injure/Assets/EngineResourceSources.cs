// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Injure.Assets;

/// <summary>
/// Engine resource source that serves files from a filesystem directory.
/// </summary>
/// <param name="root">Root directory to serve files from.</param>
public sealed class DirectoryEngineResourceSource(string root) : IEngineResourceSource {
	private readonly string root = !string.IsNullOrWhiteSpace(root) ? root : throw new ArgumentException("root must be non-null/empty/whitespace");

	public EngineResourceSourceResult TryCreate(EngineResourceID id) {
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

/// <summary>
/// Engine resource source that serves embedded assembly resources from an explicit ID set.
/// </summary>
/// <remarks>
/// The resource ID path is used as the manifest resource name. The explicit ID set is used instead of
/// reflection-based discovery so this source remains simple and NativeAOT-friendly.
/// </remarks>
public sealed class EmbeddedEngineResourceSource(Assembly assembly, HashSet<EngineResourceID> ids) : IEngineResourceSource {
	private readonly Assembly assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
	private readonly HashSet<EngineResourceID> ids = ids ?? throw new ArgumentNullException(nameof(ids));

	public EngineResourceSourceResult TryCreate(EngineResourceID id) {
		if (!ids.Contains(id))
			return EngineResourceSourceResult.NotHandled();

		// check upfront if it exists
		Stream? dummy = assembly.GetManifestResourceStream(id.Path) ??
			throw new FileNotFoundException("failed to open embedded engine resource stream", id.Path);
		dummy.Dispose();

		return EngineResourceSourceResult.Success(new EngineResourceData(
			openRead: () => assembly.GetManifestResourceStream(id.Path) ?? throw new FileNotFoundException("failed to open embedded engine resource stream", id.Path),
			debugName: id.Path,
			suggestedExtension: Path.GetExtension(id.Path),
			origin: assembly
		));
	}
}
