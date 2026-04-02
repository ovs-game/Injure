// SPDX-License-Identifier: MIT

using System;

namespace Injure.Coroutines;

public sealed class CoroutineChildFaultException(CoroutineHandle handle, Exception childEx) : Exception($"coroutine {handle} faulted", childEx) {
	public CoroutineHandle Handle { get; } = handle;
	public Exception ChildException { get; } = childEx;
}
