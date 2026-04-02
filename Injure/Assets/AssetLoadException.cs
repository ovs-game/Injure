// SPDX-License-Identifier: MIT

using System;

namespace Injure.Assets;

public /* unsealed */ class AssetLoadException : Exception {
	public AssetID AssetID { get; }
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
