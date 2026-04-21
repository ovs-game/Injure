// SPDX-License-Identifier: MIT

using System;

namespace Injure.Assets;

/// <summary>
/// Exception thrown when the entire asset pipeline has been tried but no suitable
/// source/resolver/creator has been found.
/// </summary>
public sealed class AssetUnhandledException : AssetLoadException {
	public AssetUnhandledException(AssetID id, Type type, string message) : base(id, type, message) {
	}

	public AssetUnhandledException(AssetID id, Type type, string message, Exception ex) : base(id, type, message, ex) {
	}
}
