// SPDX-License-Identifier: MIT

using System;
using System.IO;

namespace Injure.Assets;

public sealed class EngineResourceData(Func<Stream> openRead, string debugName, string? suggestedExtension = null, object? origin = null) {
	private readonly Func<Stream> openRead = openRead;

	public string DebugName { get; } = debugName;
	public string? SuggestedExtension { get; } = suggestedExtension;
	public object? Origin { get; } = origin;

	public Stream OpenRead() => openRead();
}
