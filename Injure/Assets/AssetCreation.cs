// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Injure.Analyzers.Attributes;

namespace Injure.Assets;

// ==========================================================================
// asset creation

[ClosedEnum]
public readonly partial struct AssetSourceResultKind {
	public enum Case {
		NotHandled,
		Success
	}
}

public readonly record struct AssetSourceResult(
	AssetSourceResultKind Kind,
	Stream? Stream = null
) {
	public static AssetSourceResult NotHandled() => new AssetSourceResult(AssetSourceResultKind.NotHandled);
	public static AssetSourceResult Success(Stream stream) => new AssetSourceResult(AssetSourceResultKind.Success, stream);
}

public readonly record struct AssetSourceInfo(
	AssetID AssetID
);

public interface IAssetSource {
	ValueTask<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDependencyCollector coll, CancellationToken ct = default);
}

// ==========================================================================
// asset resolution
[ClosedEnum]
public readonly partial struct AssetResolveResultKind {
	public enum Case {
		NotHandled,
		Success
	}
}

public readonly record struct AssetResolveResult(
	AssetResolveResultKind Kind,
	AssetData? Data = null
) {
	public static AssetResolveResult NotHandled() => new AssetResolveResult(AssetResolveResultKind.NotHandled);
	public static AssetResolveResult Success(AssetData data) => new AssetResolveResult(AssetResolveResultKind.Success, data);
}

public readonly record struct AssetResolveInfo(
	AssetID AssetID,
	Func<AssetID, CancellationToken, ValueTask<Stream>> FetchAsync,
	Func<AssetID, CancellationToken, ValueTask<Stream?>> TryFetchAsync
);

public interface IAssetResolver {
	ValueTask<AssetResolveResult> TryResolveAsync(AssetResolveInfo info, IAssetDependencyCollector coll, CancellationToken ct = default);
}

// ==========================================================================
// asset creation
[ClosedEnum]
public readonly partial struct AssetCreateResultKind {
	public enum Case {
		NotHandled,
		Success
	}
}

public readonly record struct AssetCreateResult<T>(
	AssetCreateResultKind Kind,
	T? Value = null
) where T : class {
	public static AssetCreateResult<T> NotHandled() => new AssetCreateResult<T>(AssetCreateResultKind.NotHandled);
	public static AssetCreateResult<T> Success(T value) => new AssetCreateResult<T>(AssetCreateResultKind.Success, value);
}

public readonly record struct AssetCreateInfo(
	AssetID AssetID,
	AssetData Data
);

public interface IAssetCreator<T> where T : class {
	ValueTask<AssetCreateResult<T>> TryCreateAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct = default);
}

public readonly record struct AssetFinalizeInfo<TPrepared>(
	AssetID AssetID,
	TPrepared Prepared
) where TPrepared : AssetPreparedData;

public readonly record struct AssetPrepareResult<TPrepared>(
	AssetCreateResultKind Kind,
	TPrepared? Prepared = null
) where TPrepared : AssetPreparedData {
	public static AssetPrepareResult<TPrepared> NotHandled() => new AssetPrepareResult<TPrepared>(AssetCreateResultKind.NotHandled);
	public static AssetPrepareResult<TPrepared> Success(TPrepared prepared) => new AssetPrepareResult<TPrepared>(AssetCreateResultKind.Success, prepared);
}

public interface IAssetStagedCreator<T, TPrepared> where T : class where TPrepared : AssetPreparedData {
	ValueTask<AssetPrepareResult<TPrepared>> TryPrepareAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct = default);
	T Finalize(AssetFinalizeInfo<TPrepared> info);
}
