// SPDX-License-Identifier: MIT

using System;

namespace Injure.Coroutines;

public enum CoroUnhandledFaultMode {
	Ignore,
	LogAfterTick,
	ThrowAfterTick,
	LogAndThrowAfterTick
}

public sealed class CoroutineUnhandledFaultInfo {
	public required Exception Exception { get; init; }
	public required CoroutineInfo Info { get; init; }
	public required CoroutineTrace Trace { get; init; }
}
