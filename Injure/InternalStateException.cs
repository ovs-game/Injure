// SPDX-License-Identifier: MIT

using System;

namespace Injure;

/// <summary>
/// Exception thrown on internal logic bugs, invariant violations, state corruption,
/// seemingly impossible conditions, bad values for purely-internal types, etc.
/// Informally speaking, you should never see this unless there's a bug in the library.
/// </summary>
public sealed class InternalStateException : Exception {
	public InternalStateException() {}
	public InternalStateException(string message) : base(message) {}
	public InternalStateException(string message, Exception ex) : base(message, ex) {}
}
