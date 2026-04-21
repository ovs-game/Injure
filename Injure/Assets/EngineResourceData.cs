// SPDX-License-Identifier: MIT

using System;
using System.IO;

namespace Injure.Assets;

/// <summary>
/// Data handle for an engine resource.
/// </summary>
public sealed class EngineResourceData(Func<Stream> openRead, string debugName, string? suggestedExtension = null, object? origin = null) {
	private readonly Func<Stream> openRead = openRead;

	public string DebugName { get; } = debugName;
	public string? SuggestedExtension { get; } = suggestedExtension;
	public object? Origin { get; } = origin;

	/// <summary>
	/// Open a fresh readable stream for this resource.
	/// </summary>
	/// <returns>A new stream positioned at the beginning of the resource data.</returns>
	/// <remarks>
	/// Returns a fresh stream every time. Callers own the returned streams and are
	/// responsible for disposing them.
	/// </remarks>
	public Stream OpenRead() => openRead();
}
