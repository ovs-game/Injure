// SPDX-License-Identifier: MIT

using System;

using Injure.Analyzers.Attributes;

namespace Injure.Coroutines;

[ClosedEnum]
public readonly partial struct CoroUnhandledFaultMode {
	public enum Case {
		Ignore,
		LogAfterTick,
		ThrowAfterTick,
		LogAndThrowAfterTick
	}
}

public sealed class CoroutineUnhandledFaultInfo {
	public required Exception Exception { get; init; }
	public required CoroutineInfo Info { get; init; }
	public required CoroutineTrace Trace { get; init; }
}
