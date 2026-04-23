// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Hexa.NET.SDL3;

namespace Injure.Core;

public sealed class SDLException(string op, string message) : Exception($"{op}: {message}") {
	public readonly string Operation = op;

	[StackTraceHidden]
	public static void Check(bool v, [CallerArgumentExpression(nameof(v))] string? expr = null) {
		if (!v)
			throw new SDLException(getfnname(expr), SDL.GetErrorS());
	}

	private static string getfnname(string? expr) {
		if (string.IsNullOrWhiteSpace(expr))
			return "<unknown SDL call>";
		expr = expr.Replace("SDL.", "SDL_");
		int paren = expr.IndexOf('(');
		return paren >= 0 ? expr[..paren].Trim() : expr.Trim();
	}
}
