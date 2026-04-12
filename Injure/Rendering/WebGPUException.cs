// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Injure.Rendering;

public sealed class WebGPUException(string op, string message) : Exception($"{op}: {message}") {
	public readonly string Operation = op;

	[StackTraceHidden]
	public static T Check<T>(T v, [CallerArgumentExpression(nameof(v))] string? expr = null) where T : unmanaged, IEquatable<T> {
		if (!v.Equals(default))
			return v;
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
