// SPDX-License-Identifier: MIT

using System;

namespace Injure;

public sealed class InternalStateException : InvalidOperationException {
	public InternalStateException() {}
	public InternalStateException(string message) : base(message) {}
	public InternalStateException(string message, Exception ex) : base(message, ex) {}
}
