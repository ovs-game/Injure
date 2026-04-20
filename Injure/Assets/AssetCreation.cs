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
		Success,
		Error
	}
}

public readonly record struct AssetSourceResult(
	AssetSourceResultKind Kind,
	Stream? Stream = null,
	Exception? Exception = null
) {
	public static AssetSourceResult NotHandled() => new AssetSourceResult(AssetSourceResultKind.NotHandled);
	public static AssetSourceResult Success(Stream stream) => new AssetSourceResult(AssetSourceResultKind.Success, stream);
	public static AssetSourceResult Error(Exception ex) => new AssetSourceResult(AssetSourceResultKind.Error, null, ex);
}

public readonly record struct AssetSourceInfo(
	AssetID AssetID
);

public interface IAssetSource {
	AssetSourceResult TrySource(AssetSourceInfo info, IAssetDependencyCollector coll);
}

public interface IAssetSourceAsync {
	Task<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDependencyCollector coll, CancellationToken ct = default);
}

// ==========================================================================
// asset resolution
[ClosedEnum]
public readonly partial struct AssetResolveResultKind {
	public enum Case {
		NotHandled,
		Success,
		Error
	}
}

public readonly record struct AssetResolveResult(
	AssetResolveResultKind Kind,
	AssetData? Data = null,
	Exception? Exception = null
) {
	public static AssetResolveResult NotHandled() => new AssetResolveResult(AssetResolveResultKind.NotHandled);
	public static AssetResolveResult Success(AssetData data) => new AssetResolveResult(AssetResolveResultKind.Success, data);
	public static AssetResolveResult Error(Exception ex) => new AssetResolveResult(AssetResolveResultKind.Error, null, ex);
}

public readonly record struct AssetResolveInfo(
	AssetID AssetID,
	Func<AssetID, Stream> Fetch
);

public readonly record struct AssetResolveAsyncInfo(
	AssetID AssetID,
	Func<AssetID, CancellationToken, Task<Stream>> FetchAsync
);

public interface IAssetResolver {
	AssetResolveResult TryResolve(AssetResolveInfo info, IAssetDependencyCollector coll);
}

public interface IAssetResolverAsync {
	Task<AssetResolveResult> TryResolveAsync(AssetResolveAsyncInfo info, IAssetDependencyCollector coll, CancellationToken ct = default);
}

// ==========================================================================
// asset creation
[ClosedEnum]
public readonly partial struct AssetCreateResultKind {
	public enum Case {
		NotHandled,
		Success,
		Error
	}
}

public readonly record struct AssetCreatePreparedResult(
	AssetCreateResultKind Kind,
	AssetPreparedData? Prepared = null,
	Exception? Exception = null
) {
	public static AssetCreatePreparedResult NotHandled() => new AssetCreatePreparedResult(AssetCreateResultKind.NotHandled);
	public static AssetCreatePreparedResult Success(AssetPreparedData prepared) => new AssetCreatePreparedResult(AssetCreateResultKind.Success, prepared);
	public static AssetCreatePreparedResult Error(Exception ex) => new AssetCreatePreparedResult(AssetCreateResultKind.Error, null, ex);
}

public readonly record struct AssetCreateResult<T>(
	AssetCreateResultKind Kind,
	T? Value = null,
	Exception? Exception = null
) where T : class {
	public static AssetCreateResult<T> NotHandled() => new AssetCreateResult<T>(AssetCreateResultKind.NotHandled);
	public static AssetCreateResult<T> Success(T value) => new AssetCreateResult<T>(AssetCreateResultKind.Success, value);
	public static AssetCreateResult<T> Error(Exception ex) => new AssetCreateResult<T>(AssetCreateResultKind.Error, null, ex);
}

public readonly record struct AssetCreateInfo(
	AssetID AssetID,
	AssetData Data
);

public readonly record struct AssetFinalizeInfo(
	AssetID AssetID,
	AssetPreparedData Prepared
);

public interface IAssetCreatorAsync<T> where T : class {
	Task<AssetCreatePreparedResult> TryCreateAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct = default);
	AssetCreateResult<T> TryFinalize(AssetFinalizeInfo info);
}
