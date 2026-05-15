// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Runtime;

public readonly record struct ExceptionSnapshot(string TypeName, string Message) {
	public override string ToString() => $"{TypeName}: {Message}";
	public static ExceptionSnapshot FromException(Exception ex) => new(ex.GetType()?.FullName ?? ex.GetType().Name, ex.Message);
	public ForeignException ToException() => new(TypeName, Message);
}
