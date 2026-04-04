// SPDX-License-Identifier: MIT

using HarfBuzzSharp;

using Injure.Graphics.Text;
using static Injure.Tests.Graphics.Text.Util;

namespace Injure.Tests.Graphics.Text;

public sealed class TextBidiTests {
	[Fact]
	public void LogicalRunsPureLTRWorks() {
		LogicalBidiRun[] runs = TextAnalysis.GetLogicalBidiRuns("abc");
		AssertLogicalRuns(runs, (0, "abc".Length, Direction.LeftToRight));
	}

	[Fact]
	public void LogicalRunsPureRTLWorks() {
		LogicalBidiRun[] runs = TextAnalysis.GetLogicalBidiRuns("אבג");
		AssertLogicalRuns(runs, (0, "אבג".Length, Direction.RightToLeft));
	}

	[Fact]
	public void LogicalRunsMixedDirWorks() {
		LogicalBidiRun[] runs = TextAnalysis.GetLogicalBidiRuns("abc אבג def");
		AssertLogicalRuns(runs,
			(0,                "abc ".Length, Direction.LeftToRight),
			("abc ".Length,    "אבג".Length,  Direction.RightToLeft),
			("abc אבג".Length, " def".Length, Direction.LeftToRight)
		);
	}

	[Fact]
	public void VisualRunsPureLTRWorks() {
		VisualBidiRun[] runs = TextAnalysis.GetVisualBidiRunsForLine("abc", 0, "abc".Length);
		AssertVisualRuns(runs, (0, "abc".Length, Direction.LeftToRight));
	}

	[Fact]
	public void VisualRunsPureRTLWorks() {
		VisualBidiRun[] runs = TextAnalysis.GetVisualBidiRunsForLine("אבג", 0, "אבג".Length);
		AssertVisualRuns(runs, (0, "אבג".Length, Direction.RightToLeft));
	}

	[Fact]
	public void VisualRunsMixedDirWorks() {
		VisualBidiRun[] runs = TextAnalysis.GetVisualBidiRunsForLine("abc אבג def", 0, "abc אבג def".Length);
		AssertVisualRuns(runs,
			(0,                "abc ".Length, Direction.LeftToRight),
			("abc ".Length,    "אבג".Length,  Direction.RightToLeft),
			("abc אבג".Length, " def".Length, Direction.LeftToRight)
		);
	}

	[Fact]
	public void VisualRunsSubrangeUsesAbsoluteIndices() {
		const string text = "abc אבג def";
		const int lineStart = 4;
		const int lineLimit = 7;
		VisualBidiRun[] runs = TextAnalysis.GetVisualBidiRunsForLine(text, lineStart, lineLimit);
		Assert.NotEmpty(runs);
		Assert.All(runs, run => Assert.InRange(run.Start, lineStart, lineLimit));
	}
}
