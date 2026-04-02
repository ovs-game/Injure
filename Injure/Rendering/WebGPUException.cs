// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Injure.Rendering;

public sealed class WebGPUException(string op, string message) : Exception($"{op}: {message}") {
	public readonly string Operation = op;

	[StackTraceHidden]
	public static unsafe T* Check<T>(T *p, [CallerArgumentExpression(nameof(p))] string? expr = null) where T : unmanaged {
		if (p is not null)
			return p;
		throw new WebGPUException(getfnname(expr), "WebGPU call returned null");
	}

	private static string getfnname(string? expr) {
		if (string.IsNullOrWhiteSpace(expr))
			return "<unknown WebGPU call>";
		int paren = expr.IndexOf('(');
		if (paren < 0)
			return expr.Trim();
		ReadOnlySpan<char> head = expr.AsSpan(0, paren).Trim();
		int dot = head.LastIndexOf('.');
		return dot >= 0 ? head[(dot + 1)..].ToString() : head.ToString();
	}
}
