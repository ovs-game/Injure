// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Injure.Assets;

/// <summary>
/// Asset source that serves files from a filesystem directory for one asset namespace.
/// </summary>
/// <param name="matchNamespace">Asset namespace handled by this source.</param>
/// <param name="root">Root directory to serve files from.</param>
/// <param name="fileOpenOptions">Options for opened file streams, or <see langword="null"/> for defaults.</param>
/// <remarks>
/// Namespace matching is ordinal and case-sensitive. Missing files return <c>NotHandled</c>;
/// failure occurs for files that exist but cannot be opened or read.
/// </remarks>
public sealed class DirectoryAssetSource(string matchNamespace, string root, FileStreamOptions? fileOpenOptions = null) : IAssetSource {
	private readonly string matchNamespace = matchNamespace;
	private readonly string root = !string.IsNullOrWhiteSpace(root) ? root : throw new ArgumentException("root must be non-null/empty/whitespace");
	private readonly FileStreamOptions fileOpenOptions = fileOpenOptions ?? DefaultFileOpenOptions;

	/// <summary>
	/// Default <see cref="FileStreamOptions"/> used for opened files if an
	/// override isn't provided.
	/// </summary>
	public static readonly FileStreamOptions DefaultFileOpenOptions = new() {
		Mode = FileMode.Open,
		Access = FileAccess.Read,
		Share = FileShare.ReadWrite | FileShare.Delete,
		Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
		BufferSize = 64 * 1024,
	};

	public ValueTask<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (info.AssetID.Namespace != matchNamespace)
			return ValueTask.FromResult(AssetSourceResult.NotHandled());
		string path = Path.Combine(root, info.AssetID.Path.Replace('/', Path.DirectorySeparatorChar));

		try {
			Stream stream = new FileStream(path, fileOpenOptions);
			coll.Add(new FileAssetDependency(Path.GetFullPath(path)));
			return ValueTask.FromResult(AssetSourceResult.Success(stream));
		} catch (FileNotFoundException) {
			return ValueTask.FromResult(AssetSourceResult.NotHandled());
		} catch (DirectoryNotFoundException) {
			return ValueTask.FromResult(AssetSourceResult.NotHandled());
		}
	}
}

/// <summary>
/// Asset source that serves embedded assembly resources from an explicit asset ID set.
/// </summary>
/// <remarks>
/// The asset ID path is used as the manifest resource name. The explicit ID set is used instead of
/// reflection-based discovery so this source remains simple and NativeAOT-friendly.
/// </remarks>
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
