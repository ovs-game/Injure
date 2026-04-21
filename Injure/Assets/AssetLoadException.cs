// SPDX-License-Identifier: MIT

using System;

namespace Injure.Assets;

/// <summary>
/// Base exception thrown when an asset load, prepare, or finalize operation fails.
/// </summary>
public /* unsealed */ class AssetLoadException : Exception {
	/// <summary>
	/// ID of the asset involved in the failed operation.
	/// </summary>
	public AssetID AssetID { get; }

	/// <summary>
	/// Type of the asset involved in the failed operation.
	/// </summary>
	public Type AssetType { get; }

	public AssetLoadException(AssetID id, Type type, string message) : base(fmt(id, type, message)) {
		AssetID = id;
		AssetType = type;
	}

	public AssetLoadException(AssetID id, Type type, string message, Exception ex) : base(fmt(id, type, message), ex) {
		AssetID = id;
		AssetType = type;
	}

	private static string fmt(AssetID id, Type type, string message) =>
		type is null ? $"{id}: {message}" : $"{type.Name}({id}): {message}";
}
