// SPDX-License-Identifier: MIT

using Injure.Graphics.Text;

namespace Injure.Tests.Graphics.Text;

public sealed class TextLayoutTests {
	[Fact]
	public void SkipLeadingWsWorks() {
		ParagraphCluster[] clusters = [
			new ParagraphCluster(0, 1, 0, 1, 10, true,  LineBreakKind.None),
			new ParagraphCluster(1, 1, 1, 1, 10, true,  LineBreakKind.None),
			new ParagraphCluster(2, 1, 2, 1, 10, false, LineBreakKind.None),
		];
		int next = TextLayouter.SkipLeadingWs(clusters, 0);
		Assert.Equal(2, next);
	}

	[Fact]
	public void TrimTrailingWsWorks() {
		ParagraphCluster[] clusters = [
			new ParagraphCluster(0, 1, 0, 1, 10, false, LineBreakKind.None),
			new ParagraphCluster(1, 1, 1, 1, 10, true,  LineBreakKind.Soft),
			new ParagraphCluster(2, 1, 2, 1, 10, true,  LineBreakKind.Soft),
		];
		int trimmed = TextLayouter.TrimTrailingWs(clusters, 0, 3);
		Assert.Equal(1, trimmed);
	}

	[Fact]
	public void FlattenSourceOrderClustersWorks() {
		ParagraphRun[] runs = [
			new ParagraphRun {
				Font = null!,
				Item = default,
				ShapedRun = null!,
				SourceOrderClusters = [
					new ParagraphCluster(0, 1, 0, 1, 10, false, LineBreakKind.None),
					new ParagraphCluster(1, 1, 1, 1, 10, false, LineBreakKind.None),
				],
				GlyphOrderClusters = null!
			},
			new ParagraphRun {
				Font = null!,
				Item = default,
				ShapedRun = null!,
				SourceOrderClusters = [
					new ParagraphCluster(0, 1, 2, 1, 10, false, LineBreakKind.None),
				],
				GlyphOrderClusters = null!
			}
		];
		ParagraphCluster[] flat = TextLayouter.FlattenSourceOrderClusters(runs);
		Assert.Equal(3, flat.Length);
		Assert.Equal(0, flat[0].SourceStart);
		Assert.Equal(1, flat[1].SourceStart);
		Assert.Equal(2, flat[2].SourceStart);
	}

	[Fact]
	public void WrapParaLogicalLinesEmptyParaYieldsSingleEmptyLine() {
		LogicalLine[] lines = TextLayouter.WrapParaLogicalLines(
			paraClusters: [],
			paraAbsoluteStart: 0,
			paraLength: 0,
			maxWidth: 100f,
			wrapMode: TextWrapMode.Greedy
		);
		Assert.Single(lines);
		Assert.Equal(0, lines[0].Start);
		Assert.Equal(0, lines[0].Limit);
	}

	[Fact]
	public void WrapParaLogicalLinesWorks() {
		ParagraphCluster[] clusters = [
			new ParagraphCluster(0, 1, 0, 1, 10, false, LineBreakKind.None),
			new ParagraphCluster(1, 1, 1, 1, 10, false, LineBreakKind.None),
			new ParagraphCluster(2, 1, 2, 1, 10, true,  LineBreakKind.Soft),
			new ParagraphCluster(3, 1, 3, 1, 10, false, LineBreakKind.None),
			new ParagraphCluster(4, 1, 4, 1, 10, false, LineBreakKind.None),
		];
		LogicalLine[] lines = TextLayouter.WrapParaLogicalLines(
			clusters,
			paraAbsoluteStart: 0,
			paraLength: 5,
			maxWidth: 25f,
			wrapMode: TextWrapMode.Greedy
		);
		Assert.True(lines.Length >= 2);
		Assert.Equal(0, lines[0].Start);
		Assert.True(lines[0].Limit <= 3);
	}
}
