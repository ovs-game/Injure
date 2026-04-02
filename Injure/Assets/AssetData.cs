// SPDX-License-Identifier: MIT

namespace Injure.Assets;

public abstract class AssetData(string debugName, string? suggestedExtension = null, object? origin = null) {
	public readonly string DebugName = debugName;
	public readonly string? SuggestedExtension = suggestedExtension;
	public readonly object? Origin = origin;
}

public abstract class AssetPreparedData() {}
