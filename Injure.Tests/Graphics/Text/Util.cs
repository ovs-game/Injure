// SPDX-License-Identifier: MIT

using System;
using HarfBuzzSharp;

using Injure.Graphics.Text;

namespace Injure.Tests.Graphics.Text;

internal static class Util {
	public static void AssertSpans(ReadOnlySpan<GraphemeSpan> actual, params (int Start, int Length)[] expected) {
		Assert.Equal(expected.Length, actual.Length);
		for (int i = 0; i < expected.Length; i++) {
			Assert.Equal(expected[i].Start, actual[i].Start);
			Assert.Equal(expected[i].Length, actual[i].Length);
		}
	}

	public static void AssertBreaks(ReadOnlySpan<LineBreakOpportunity> actual, params (int Position, LineBreakKind Kind)[] expected) {
		Assert.Equal(expected.Length, actual.Length);
		for (int i = 0; i < expected.Length; i++) {
			Assert.Equal(expected[i].Position, actual[i].Position);
			Assert.Equal(expected[i].Kind, actual[i].Kind);
		}
	}

	public static void AssertLogicalRuns(ReadOnlySpan<LogicalBidiRun> actual, params (int Start, int Length, Direction Dir)[] expected) {
		Assert.Equal(expected.Length, actual.Length);
		for (int i = 0; i < expected.Length; i++) {
			Assert.Equal(expected[i].Start, actual[i].Start);
			Assert.Equal(expected[i].Length, actual[i].Length);
			Assert.Equal(expected[i].Dir, actual[i].Direction);
		}
	}

	public static void AssertVisualRuns(ReadOnlySpan<VisualBidiRun> actual, params (int Start, int Length, Direction Dir)[] expected) {
		Assert.Equal(expected.Length, actual.Length);
		for (int i = 0; i < expected.Length; i++) {
			Assert.Equal(expected[i].Start, actual[i].Start);
			Assert.Equal(expected[i].Length, actual[i].Length);
			Assert.Equal(expected[i].Dir, actual[i].Direction);
		}
	}

	public static string[] LineTexts(string fullText, ReadOnlySpan<LogicalLine> lines) {
		string[] result = new string[lines.Length];
		for (int i = 0; i < lines.Length; i++)
			result[i] = fullText[lines[i].Start..lines[i].Limit];
		return result;
	}
}
