// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Injure.Coroutines;

public sealed class CoroutineUnhandledFaultsException(IReadOnlyList<CoroutineUnhandledFaultInfo> faults) : Exception($"{faults.Count} unhandled coroutine faults occurred") {
	public IReadOnlyList<CoroutineUnhandledFaultInfo> Faults { get; } = faults;
}
