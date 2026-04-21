// SPDX-License-Identifier: MIT

using System;

namespace Injure.Assets;

/// <summary>
/// Resolved asset data passed from resolvers to creators.
/// </summary>
/// <remarks>
/// Resolver success transfers ownership of the data object to the asset store. The store disposes it
/// after creator attempts complete. As such, creators must not hold onto this object beyond the create
/// call unless explicitly supported by the specific asset data type.
/// </remarks>
public abstract class AssetData(string debugName, string? suggestedExtension = null, object? origin = null) : IDisposable {
	/// <summary>
	/// Human-readable name, used for diagnostics.
	/// </summary>
	public string DebugName { get; } = debugName;

	/// <summary>
	/// Suggested file extension or format hint (if known), used for diagnostics.
	/// </summary>
	public string? SuggestedExtension { get; } = suggestedExtension;

	/// <summary>
	/// Implementation-defined origin object (if known), used for diagnostics.
	/// </summary>
	public object? Origin { get; } = origin;

	/// <summary>
	/// Releases any resources held by this resolved data object.
	/// </summary>
	public virtual void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// Intermediate prepared data produced by a staged creator for the finalize stage.
/// </summary>
/// <remarks>
/// Prepare success transfers ownership of the data object to the asset store. The store disposes it
/// after finalization succeeds/fails or when a prepared reload is superseded before publication. As
/// such, creators must not hold onto this object beyond the finalize call unless explicitly supported
/// by the specific prepared asset data type.
/// </remarks>
public abstract class AssetPreparedData() : IDisposable {
	/// <summary>
	/// Releases any resources held by this prepared data object.
	/// </summary>
	public virtual void Dispose() => GC.SuppressFinalize(this);
}
