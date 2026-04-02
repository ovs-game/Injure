// SPDX-License-Identifier: MIT

namespace Injure.Assets;

public readonly record struct EngineResourceID(string Path) {
	public override string ToString() => Path;
}
