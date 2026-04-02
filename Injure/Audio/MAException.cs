// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Miniaudio;

using static Miniaudio.ma_result;

namespace Injure.Audio;

public sealed class MAException(string fn, ma_result result) : Exception($"{fn}: {result}") {
	public readonly string Function = fn;
	public readonly ma_result Result = result;

	[StackTraceHidden]
	public static void Check(ma_result r, [CallerArgumentExpression(nameof(r))] string? expr = null) {
		if (r == MA_SUCCESS)
			return;
		throw new MAException(getfnname(expr), r);
	}

	private static string getfnname(string? expr) {
		if (string.IsNullOrWhiteSpace(expr))
			return "<unknown MA call>";
		expr = expr.Replace("ma.", "ma_");
		int paren = expr.IndexOf('(');
		return paren >= 0 ? expr[..paren].Trim() : expr.Trim();
	}
}
