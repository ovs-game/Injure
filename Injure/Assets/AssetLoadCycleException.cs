// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Injure.Assets;

public sealed class AssetLoadCycleException(AssetID id, Type type, string message, IReadOnlyList<AssetKey> cycle) :
	AssetLoadException(id, type, message) {
	public IReadOnlyList<AssetKey> Cycle { get; } = cycle;
}
