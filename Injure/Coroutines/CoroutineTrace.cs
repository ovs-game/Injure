// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Injure.Coroutines;

public readonly struct CoroutineTraceFrame {
	public required string DebugName { get; init; }
	public required string EnumeratorTypeName { get; init; }
	public required string SourceFile { get; init; }
	public required int SourceLine { get; init; }
	public required string SourceMember { get; init; }
}

public sealed class CoroutineTrace {
	public required CoroutineHandle Handle { get; init; }
	public required string? Name { get; init; }
	public required string? ScopeName { get; init; }
	public required string? CurrentWaitDebugDescription { get; init; }
	public required IReadOnlyList<CoroutineTraceFrame> Frames { get; init; }
}
