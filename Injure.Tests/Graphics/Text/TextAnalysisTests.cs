// SPDX-License-Identifier: MIT

using Injure.Graphics.Text;
using static Injure.Tests.Graphics.Text.Util;

namespace Injure.Tests.Graphics.Text;

[Collection("needsnative")]
public sealed class TextAnalysisTests {
	public static readonly TheoryData<string, (int Start, int Length)[]> GraphemeCases = new() {
		{ "a", new[] { (0, 1) } },
		{ "ab", new[] { (0, 1), (1, 1) } },
		{ "e\u0301", new[] { (0, 2) } },
		{ "A\u030A", new[] { (0, 2) } },
		{ "x\r\ny", new[] { (0, 1), (1, 2), (3, 1) } },
	};

	[Theory]
	[MemberData(nameof(GraphemeCases))]
	public void GetGraphemeSpansWorks(string text, (int Start, int Length)[] expected) {
		GraphemeSpan[] spans = TextAnalysis.GetGraphemeSpans(text);
		AssertSpans(spans, expected);
	}

	[Fact]
	public void GetLineBreaksWorks() {
		LineBreakOpportunity[] breaks = TextAnalysis.GetLineBreaks("abc def ghi", locale: "en");
		AssertBreaks(breaks, (4, LineBreakKind.Soft), (8, LineBreakKind.Soft));
	}

	[Fact]
	public void GetLineBreaksHandlesNbsp() {
		LineBreakOpportunity[] breaks = TextAnalysis.GetLineBreaks("abc\u00A0def", locale: "en");
		Assert.Empty(breaks);
	}
}
