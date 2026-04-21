// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// Unique engine resource identifier.
/// </summary>
/// <param name="Path">Resource path.</param>
/// <remarks>
/// There is currently no path validation; this is temporary and stricter
/// <see cref="AssetID"/>-like validation in the future is expected.
/// </remarks>
public readonly record struct EngineResourceID(string Path) {
	/// <summary>Returns <see cref="Path"/>.</summary>
	public override string ToString() => Path;
}
