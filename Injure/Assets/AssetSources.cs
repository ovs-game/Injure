// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Injure.Assets;

public sealed class DirectoryAssetSource(string matchNamespace, string root) : IAssetSource {
	private readonly string matchNamespace = matchNamespace;
	private readonly string root = !string.IsNullOrWhiteSpace(root) ? root : throw new ArgumentException("root must be non-null/empty/whitespace");

	public ValueTask<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (info.AssetID.Namespace != matchNamespace)
			return ValueTask.FromResult(AssetSourceResult.NotHandled());
		string path = Path.Combine(root, info.AssetID.Path.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(path))
			return ValueTask.FromResult(AssetSourceResult.NotHandled());

		Stream stream = new FileStream(path, new FileStreamOptions {
			Mode = FileMode.Open,
			Access = FileAccess.Read,
			Share = FileShare.Read,
			Options = FileOptions.Asynchronous
		});
		coll.Add(new FileAssetDependency(Path.GetFullPath(path)));
		return ValueTask.FromResult(AssetSourceResult.Success(stream));
	}
}

// this one's just async for api consistency, there's no real reason here honestly
public sealed class EmbeddedAssetSource(Assembly assembly, HashSet<AssetID> ids) : IAssetSource {
	private readonly Assembly assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
	private readonly HashSet<AssetID> ids = ids ?? throw new ArgumentNullException(nameof(ids));

	public ValueTask<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (!ids.Contains(info.AssetID))
			return ValueTask.FromResult(AssetSourceResult.NotHandled());

		Stream stream = assembly.GetManifestResourceStream(info.AssetID.Path) ??
			throw new FileNotFoundException("failed to open embedded asset stream", info.AssetID.Path);
		coll.Add(new EmbeddedAssetDependency(assembly, info.AssetID.Path));
		return ValueTask.FromResult(AssetSourceResult.Success(stream));
	}
}
