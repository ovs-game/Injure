// SPDX-License-Identifier: MIT

using System;

namespace Injure.Assets;

public sealed class AssetUnhandledException : Exception {
	public AssetID AssetID { get; }
	public Type AssetType { get; }

	public AssetUnhandledException(AssetID id, Type type, string message) : base(fmt(id, type, message)) {
		AssetID = id;
		AssetType = type;
	}

	public AssetUnhandledException(AssetID id, Type type, string message, Exception ex)
			: base(fmt(id, type, message), ex) {
		AssetID = id;
		AssetType = type;
	}

	private static string fmt(AssetID id, Type type, string message) =>
		type is null ? $"{id}: {message}" : $"{type.Name} {id}: {message}";
}
