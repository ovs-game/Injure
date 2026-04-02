// SPDX-License-Identifier: MIT

using System.Numerics;
using HarfBuzzSharp;

namespace Injure.Graphics.Text;

public enum TextWrapMode {
	None,
	Greedy
}

internal enum LineBreakKind {
	None,
	Soft,
	Hard
}

internal readonly record struct LineBreakOpportunity(
	int Position,
	LineBreakKind Kind
);

internal readonly record struct LogicalBidiRun(
	int Start,
	int Length,
	Direction Direction
);

internal readonly record struct VisualBidiRun(
	int Start,
	int Length,
	Direction Direction
);

internal readonly record struct ParagraphCluster(
	int GlyphStart,
	int GlyphCount,
	int SourceStart,
	int SourceLength,
	float Width,
	bool IsWhitespaceOnly,
	LineBreakKind BreakAfter) {
	internal int SourceLimit => SourceStart + SourceLength;
}

internal sealed class ParagraphRun {
	public required IResolvedFont Font { get; init; }
	public required TextItem Item { get; init; }
	public required ShapedRun ShapedRun { get; init; }
	public required ParagraphCluster[] SourceOrderClusters { get; init; }
	public required ParagraphCluster[] GlyphOrderClusters { get; init; }

	public int SourceStart => Item.SourceStart;
	public int SourceLength => Item.Text.Length;
	public int SourceLimit => Item.SourceStart + Item.Text.Length;
}

internal readonly record struct LogicalLine(
	int Start,
	int Limit
);

public readonly record struct TextGlyph(
	Texture2D Atlas,
	RectI SrcPixels,
	RectF DstPixels,
	Color32 Color,
	uint GlyphID,
	uint Cluster
);

public readonly record struct TextLine(
	int GlyphStart,
	int GlyphCount,
	float Width,
	float BaselineY
);

public sealed class TextLayout {
	public required TextGlyph[] Glyphs { get; init; }
	public required TextLine[] Lines { get; init; }
	public required float Width { get; init; }
	public required float Height { get; init; }

	public void Render(Canvas cv, Vector2 at = default) {
		foreach (TextGlyph glyph in Glyphs) {
			RectF dst = new RectF(glyph.DstPixels.X + at.X, glyph.DstPixels.Y + at.Y, glyph.DstPixels.Width, glyph.DstPixels.Height);
			RectF src = new RectF(glyph.SrcPixels.X, glyph.SrcPixels.Y, glyph.SrcPixels.Width, glyph.SrcPixels.Height);
			using (cv.PushParams(Material: CanvasMaterials.RMask))
				cv.TexWithSourceRect(glyph.Atlas, dst, src, glyph.Color);
		}
	}
}
