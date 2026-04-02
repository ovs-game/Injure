// SPDX-License-Identifier: MIT

namespace Injure.Coroutines;

public enum CoroutineStatus {
	Running,
	Paused,
	Completed,
	Cancelled,
	Faulted
}

public enum CoroCancellationReason {
	ManualStop,
	OwnerRemoved,
	ScopeCancelled,
	ScopeEnded,
	Timeout,
	FaultPropagation
}

public enum CoroUpdatePhase {
	Update
}
