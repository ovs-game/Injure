// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Injure.Analyzers.Attributes;

namespace Injure.Assets;

// ==========================================================================
// asset sourcing

/// <summary>
/// Result kind returned by asset sources.
/// </summary>
[ClosedEnum]
public readonly partial struct AssetSourceResultKind {
	/// <summary>Raw switch tag for <see cref="AssetSourceResultKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// The source does not provide the requested asset.
		/// </summary>
		NotHandled,

		/// <summary>
		/// The source successfully provided a stream for the requested asset.
		/// </summary>
		Success
	}
}

/// <summary>
/// Result returned by an asset source.
/// </summary>
/// <param name="Kind">Result kind.</param>
/// <param name="Stream">On success, stream containing the source data.</param>
public readonly record struct AssetSourceResult(
	AssetSourceResultKind Kind,
	Stream? Stream = null
) {
	/// <summary>Factory for a <see cref="AssetSourceResultKind.NotHandled"/> result.</summary>
	public static AssetSourceResult NotHandled() => new AssetSourceResult(AssetSourceResultKind.NotHandled);

	/// <summary>Factory for a <see cref="AssetSourceResultKind.Success"/> result.</summary>
	public static AssetSourceResult Success(Stream stream) => new AssetSourceResult(AssetSourceResultKind.Success, stream);
}

/// <summary>
/// Information passed to an asset source.
/// </summary>
/// <param name="AssetID">Asset ID being requested.</param>
public readonly record struct AssetSourceInfo(
	AssetID AssetID
);

/// <summary>
/// Provides raw source streams for requested asset IDs.
/// </summary>
/// <remarks>
/// Returning <c>NotHandled</c> means the next source should be tried; sources should return it
/// for assets they do not provide. Failures, including recognized-but-unreadable data, are reported
/// by throwing. On success, stream ownership transfers to the asset pipeline.
/// </remarks>
public interface IAssetSource {
	/// <summary>
	/// Attempts to open source data for an asset.
	/// </summary>
	/// <param name="info">Source request information.</param>
	/// <param name="coll">Dependency collector for dependencies discovered by this source.</param>
	/// <param name="ct">Cancellation token.</param>
	ValueTask<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDependencyCollector coll, CancellationToken ct = default);
}

// ==========================================================================
// asset resolution

/// <summary>
/// Result kind returned by asset resolvers.
/// </summary>
[ClosedEnum]
public readonly partial struct AssetResolveResultKind {
	/// <summary>Raw switch tag for <see cref="AssetResolveResultKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// The resolver does not recognize or cannot handle the source data.
		/// </summary>
		NotHandled,

		/// <summary>
		/// The resolver successfully produced asset data for the creation stage.
		/// </summary>
		Success
	}
}

/// <summary>
/// Result returned by an asset resolver.
/// </summary>
/// <param name="Kind">Result kind.</param>
/// <param name="Data">On success, resolved asset data.</param>
public readonly record struct AssetResolveResult(
	AssetResolveResultKind Kind,
	AssetData? Data = null
) {
	/// <summary>Factory for a <see cref="AssetResolveResultKind.NotHandled"/> result.</summary>
	public static AssetResolveResult NotHandled() => new AssetResolveResult(AssetResolveResultKind.NotHandled);

	/// <summary>Factory for a <see cref="AssetResolveResultKind.Success"/> result.</summary>
	public static AssetResolveResult Success(AssetData data) => new AssetResolveResult(AssetResolveResultKind.Success, data);
}

/// <summary>
/// Information and source-fetch helpers passed to an asset resolver.
/// </summary>
/// <param name="AssetID">Asset ID being resolved.</param>
/// <param name="FetchAsync">Fetches required source data for another asset ID.</param>
/// <param name="TryFetchAsync">Fetches optional source data for another asset ID, or returns <see langword="null"/> if no source handles it.</param>
/// <remarks>
/// Streams returned by fetch helpers are seekable and owned by the resolver. Resolvers must dispose
/// each stream they fetch.
/// </remarks>
public readonly record struct AssetResolveInfo(
	AssetID AssetID,
	Func<AssetID, CancellationToken, ValueTask<Stream>> FetchAsync,
	Func<AssetID, CancellationToken, ValueTask<Stream?>> TryFetchAsync
);

/// <summary>
/// Converts raw source data into typed asset data that creators can consume.
/// </summary>
public interface IAssetResolver {
	/// <summary>
	/// Attempts to resolve raw source data into asset data.
	/// </summary>
	/// <param name="info">Resolve request information and fetch helpers.</param>
	/// <param name="coll">Dependency collector for dependencies discovered by this resolver.</param>
	/// <param name="ct">Cancellation token.</param>
	ValueTask<AssetResolveResult> TryResolveAsync(AssetResolveInfo info, IAssetDependencyCollector coll, CancellationToken ct = default);
}

// ==========================================================================
// asset creation

/// <summary>
/// Result kind returned by asset creators or the prepare stage of staged asset creators.
/// </summary>
[ClosedEnum]
public readonly partial struct AssetCreateResultKind {
	/// <summary>Raw switch tag for <see cref="AssetCreateResultKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// The creator does not handle the resolved data.
		/// </summary>
		NotHandled,

		/// <summary>
		/// The creator successfully produced the asset or, for staged creators,
		/// the prepared data for the finalize stage.
		/// </summary>
		Success
	}
}

/// <summary>
/// Result returned by a direct asset creator.
/// </summary>
/// <typeparam name="T">Asset value type produced by the creator.</typeparam>
/// <param name="Kind">Result kind.</param>
/// <param name="Value">On success, the created asset value.</param>
public readonly record struct AssetCreateResult<T>(
	AssetCreateResultKind Kind,
	T? Value = null
) where T : class {
	/// <summary>Factory for a <see cref="AssetCreateResultKind.NotHandled"/> result.</summary>
	public static AssetCreateResult<T> NotHandled() => new AssetCreateResult<T>(AssetCreateResultKind.NotHandled);

	/// <summary>Factory for a <see cref="AssetCreateResultKind.Success"/> result.</summary>
	public static AssetCreateResult<T> Success(T value) => new AssetCreateResult<T>(AssetCreateResultKind.Success, value);
}

/// <summary>
/// Information passed to an asset creator.
/// </summary>
/// <param name="AssetID">Asset ID being created.</param>
/// <param name="Data">Resolved asset data.</param>
public readonly record struct AssetCreateInfo(
	AssetID AssetID,
	AssetData Data
);

/// <summary>
/// Creates asset values directly from resolved asset data.
/// </summary>
/// <typeparam name="T">Asset value type produced by this creator.</typeparam>
public interface IAssetCreator<T> where T : class {
	/// <summary>
	/// Attempts to create an asset value.
	/// </summary>
	/// <param name="info">Create request information.</param>
	/// <param name="coll">Dependency collector for dependencies discovered by this creator.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <remarks>
	/// The returned value should be treated as owned by the asset store after success.
	/// </remarks>
	ValueTask<AssetCreateResult<T>> TryCreateAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct = default);
}

// ==========================================================================
// staged asset creation

/// <summary>
/// Information passed to the finalize stage of a staged asset creator.
/// </summary>
/// <typeparam name="TPrepared">Prepared-data type consumed by finalization.</typeparam>
/// <param name="AssetID">Asset ID being finalized.</param>
/// <param name="Prepared">Prepared data produced by the finalize stage.</param>
public readonly record struct AssetFinalizeInfo<TPrepared>(
	AssetID AssetID,
	TPrepared Prepared
) where TPrepared : AssetPreparedData;

/// <summary>
/// Result returned by the prepare stage of a staged asset creator.
/// </summary>
/// <typeparam name="TPrepared">Prepared-data type produced by the prepare stage.</typeparam>
/// <param name="Kind">Result kind.</param>
/// <param name="Prepared">On success, the prepared data.</param>
public readonly record struct AssetPrepareResult<TPrepared>(
	AssetCreateResultKind Kind,
	TPrepared? Prepared = null
) where TPrepared : AssetPreparedData {
	/// <summary>Factory for a <see cref="AssetCreateResultKind.NotHandled"/> result.</summary>
	public static AssetPrepareResult<TPrepared> NotHandled() => new AssetPrepareResult<TPrepared>(AssetCreateResultKind.NotHandled);

	/// <summary>Factory for a <see cref="AssetCreateResultKind.Success"/> result.</summary>
	public static AssetPrepareResult<TPrepared> Success(TPrepared prepared) => new AssetPrepareResult<TPrepared>(AssetCreateResultKind.Success, prepared);
}

/// <summary>
/// Creates asset values from resolved asset data in two stages, prepare and finalize.
/// </summary>
/// <typeparam name="T">Asset value type produced by this creator.</typeparam>
/// <typeparam name="TPrepared">Prepared-data type produced by preparation and consumed by finalization.</typeparam>
/// <remarks>
/// Useful when most work can be done asynchronously but final asset value construction must
/// happen synchronously during reload publication, for example when creating GPU resources.
/// </remarks>
public interface IAssetStagedCreator<T, TPrepared> where T : class where TPrepared : AssetPreparedData {
	/// <summary>
	/// Attempts to prepare data for a later finalize step.
	/// </summary>
	/// <param name="info">Create request information.</param>
	/// <param name="coll">Dependency collector for dependencies discovered during preparation.</param>
	/// <param name="ct">Cancellation token.</param>
	ValueTask<AssetPrepareResult<TPrepared>> TryPrepareAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct = default);

	/// <summary>
	/// Finalizes a prepared asset value.
	/// </summary>
	/// <param name="info">Finalize request information.</param>
	/// <remarks>
	/// The asset store disposes the prepared data after this method returns or throws. If finalization
	/// transfers resources out of the prepared object, the prepared object should implement that transfer
	/// explicitly.
	/// </remarks>
	T Finalize(AssetFinalizeInfo<TPrepared> info);
}
