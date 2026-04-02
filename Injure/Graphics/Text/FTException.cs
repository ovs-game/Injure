// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using FreeTypeSharp;

namespace Injure.Graphics.Text;

public sealed class FTException(string fn, FT_Error result) : Exception($"{fn}: {fmt(result)}") {
	public readonly string Function = fn;
	public readonly FT_Error Result = result;

	private static string fmt(FT_Error r) {
		string? s = Enum.GetName<FT_Error>(r);
		if (s is null)
			return $"0x{(int)r:X}";
		return s;
	}

	[StackTraceHidden]
	public static void Check(FT_Error r, [CallerArgumentExpression(nameof(r))] string? expr = null) {
		if (r == 0)
			return;
		throw new FTException(getfnname(expr), r);
	}

	private static string getfnname(string? expr) {
		if (string.IsNullOrWhiteSpace(expr))
			return "<unknown FT call>";
		int paren = expr.IndexOf('(');
		return paren >= 0 ? expr[..paren].Trim() : expr.Trim();
	}
}
