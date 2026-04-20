// SPDX-License-Identifier: MIT

using System;

namespace Injure.Assets;

public abstract class AssetData(string debugName, string? suggestedExtension = null, object? origin = null) : IDisposable {
	public string DebugName { get; }= debugName;
	public string? SuggestedExtension { get; } = suggestedExtension;
	public object? Origin { get; } = origin;

	public virtual void Dispose() => GC.SuppressFinalize(this);
}

public abstract class AssetPreparedData() {}
