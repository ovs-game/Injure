// SPDX-License-Identifier: MIT

using System;

namespace Injure.Coroutines;

public readonly struct CoroutineInfo {
	public required CoroutineHandle Handle { get; init; }
	public required string? Name { get; init; }
	public required object? Owner { get; init; }
	public required CoroutineScope? Scope { get; init; }
	public required CoroutineStatus Status { get; init; }
	public required CoroUpdatePhase LastPhase { get; init; }
	public required CoroutineTick StartTick { get; init; }
	public required CoroutineTick TerminalTick { get; init; }
	public required int StackDepth { get; init; }
	public required string? CurrentWaitDebugDescription { get; init; }
	public required Exception? Fault { get; init; }
	public required CoroCancellationReason? CancellationReason { get; init; }
}
