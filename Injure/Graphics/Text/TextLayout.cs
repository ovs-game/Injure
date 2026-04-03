// SPDX-License-Identifier: MIT

using System;
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

internal readonly record struct TextGlyph(
	GlyphAtlasPage Page,
	RectI SrcPixels,
	RectF DstPixels,
	Color32 Color,
	uint GlyphID,
	uint Cluster
);

internal readonly record struct TextLine(
	int GlyphStart,
	int GlyphCount,
	float Width,
	float BaselineY
);

public sealed class TextLayout : IDisposable {
	internal TextGlyph[] Glyphs { get; }
	internal TextLine[] Lines { get; }
	public float Width { get; }
	public float Height { get; }

	private GlyphAtlasPage[] retainedPages;
	private bool disposed = false;

	internal TextLayout(TextGlyph[] glyphs, TextLine[] lines, float width, float height, GlyphAtlasPage[] retainedPages) {
		Glyphs = glyphs;
		Lines = lines;
		Width = width;
		Height = height;
		this.retainedPages = retainedPages;
		foreach (GlyphAtlasPage p in retainedPages)
			p.Retain();
	}

	public void Render(Canvas cv, Vector2 at = default) {
		ObjectDisposedException.ThrowIf(disposed, this);
		foreach (TextGlyph glyph in Glyphs) {
			RectF dst = new RectF(glyph.DstPixels.X + at.X, glyph.DstPixels.Y + at.Y, glyph.DstPixels.Width, glyph.DstPixels.Height);
			RectF src = new RectF(glyph.SrcPixels.X, glyph.SrcPixels.Y, glyph.SrcPixels.Width, glyph.SrcPixels.Height);
			using (cv.PushParams(Material: CanvasMaterials.RMask))
				cv.TexWithSourceRect(glyph.Page.Texture, dst, src, glyph.Color);
		}
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		foreach (GlyphAtlasPage p in retainedPages)
			p.Release();
		retainedPages = Array.Empty<GlyphAtlasPage>();
	}
}
