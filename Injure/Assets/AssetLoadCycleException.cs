// SPDX-License-Identifier: MIT

using System;
using System.Collections.Immutable;

namespace Injure.Assets;

/// <summary>
/// Exception thrown when recursive asset loading would create a cycle.
/// </summary>
/// <remarks>
/// Informally speaking, honestly, you probably shouldn't even be loading full assets
/// inside an asset pipeline component.
/// </remarks>
public sealed class AssetLoadCycleException(AssetID id, Type type, string message, ImmutableArray<AssetKey> cycle) :
	AssetLoadException(id, type, message) {
	/// <summary>
	/// The detected cycle, in traversal order.
	/// </summary>
	public ImmutableArray<AssetKey> Cycle { get; } = cycle;
}
