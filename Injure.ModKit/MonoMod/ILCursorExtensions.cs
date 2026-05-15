// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Injure.ModKit.MonoMod;

public static class ILCursorExtensions {
	extension(ILCursor c) {
		public void RequireGotoNext(string expected, MoveType moveType = MoveType.Before, params Func<Instruction, bool>[] predicates) {
			int start = c.Index;
			if (c.TryGotoNext(moveType, predicates))
				return;

			string target = c.Context.Method.FullName;
			string startStr = start.ToString(CultureInfo.InvariantCulture);
			throw new ILPatternNotFoundException(
				$"expected to match IL pattern '{expected}' in method '{target}' searching forward from instruction index {startStr}",
				target,
				expected,
				startStr,
				"forward"
			);
		}
		public void RequireGotoNext(string expected, params Func<Instruction, bool>[] predicates) =>
			c.RequireGotoNext(expected, moveType: MoveType.Before, predicates);

		public void RequireGotoPrev(string expected, MoveType moveType = MoveType.Before, params Func<Instruction, bool>[] predicates) {
			int start = c.Index;
			if (c.TryGotoPrev(moveType, predicates))
				return;

			string target = c.Context.Method.FullName;
			string startStr = start.ToString(CultureInfo.InvariantCulture);
			throw new ILPatternNotFoundException(
				$"expected to match IL pattern '{expected}' in method '{target}' searching backward from instruction index {startStr}",
				target,
				expected,
				startStr,
				"backward"
			);
		}
		public void RequireGotoPrev(string expected, params Func<Instruction, bool>[] predicates) =>
			c.RequireGotoPrev(expected, moveType: MoveType.Before, predicates);
	}
}
