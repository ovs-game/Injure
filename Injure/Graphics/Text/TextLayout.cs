// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Numerics;
using HarfBuzzSharp;

using Injure.Analyzers.Attributes;

namespace Injure.Graphics.Text;

[ClosedEnum]
public readonly partial struct TextWrapMode {
	public enum Case {
		None,
		Greedy
	}
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

internal readonly record struct PlannedGlyph(
	IResolvedFont Font,
	uint GlyphID,
	uint Cluster,
	float X,
	float Y
);

internal static class TextLayouter {
	public static FontLineMetrics ComputeLineMetrics(ReadOnlySpan<ParagraphRun> paraRuns, LogicalLine logiLine, FontLineMetrics fallback) {
		float ascent = 0f;
		float descent = 0f;
		float lineGap = 0f;
		bool any = false;
		foreach (ParagraphRun paraRun in paraRuns) {
			if (!Overlaps(paraRun, logiLine.Start, logiLine.Limit))
				continue;
			FontLineMetrics m = paraRun.Font.GetState().LineMetrics;
			ascent = Math.Max(ascent, m.Ascent);
			descent = Math.Max(descent, m.Descent);
			lineGap = Math.Max(lineGap, m.LineGap);
			any = true;
		}
		return any ? new FontLineMetrics(ascent, descent, lineGap, Height: ascent + descent + lineGap) : fallback;
	}

	public static ParagraphRun[] BuildParaRuns(ITextItemizer itemizer, FallbackResolver fallbackResolver,
		ResolvedFontFallbackChain fonts, string paraText, int paraAbsoluteStart, string locale, string? languageBCP47) {
		LineBreakOpportunity[] brklist = TextAnalysis.GetLineBreaks(paraText, locale);
		Dictionary<int, LineBreakKind> breaks = BuildBreaksDict(brklist);
		LogicalBidiRun[] bidiRuns = TextAnalysis.GetLogicalBidiRuns(paraText);
		List<ParagraphRun> paraRuns = new List<ParagraphRun>();
		foreach (LogicalBidiRun bidiRun in bidiRuns) {
			ReadOnlySpan<char> bidiRunText = paraText.AsSpan(bidiRun.Start, bidiRun.Length);
			TextItem[] items = itemizer.Itemize(bidiRunText, paraAbsoluteStart + bidiRun.Start, bidiRun.Direction, languageBCP47);
			foreach (TextItem item in items) {
				ResolvedItem[] resolvedItems = fallbackResolver.ResolveItems(fonts, item);
				foreach (ResolvedItem resolved in resolvedItems)
					paraRuns.Add(BuildParaRun(resolved.Font, resolved.Item, resolved.ShapedRun, paraAbsoluteStart, breaks));
			}
		}
		return paraRuns.ToArray();
	}

	public static ParagraphRun BuildParaRun(IResolvedFont font, in TextItem item, ShapedRun shapedRun, int paraAbsoluteStart, IReadOnlyDictionary<int, LineBreakKind> breaks) {
		ParagraphCluster[] sourceOrderClusters = BuildParaClusters(item, shapedRun.SourceOrderClusters, paraAbsoluteStart, breaks);
		ParagraphCluster[] glyphOrderClusters = BuildParaClusters(item, shapedRun.GlyphOrderClusters, paraAbsoluteStart, breaks);
		return new ParagraphRun {
			Font = font,
			Item = item,
			ShapedRun = shapedRun,
			SourceOrderClusters = sourceOrderClusters,
			GlyphOrderClusters = glyphOrderClusters,
		};
	}

	public static ParagraphCluster[] BuildParaClusters(in TextItem item, ShapedCluster[] shapedClusters,
		int paraAbsoluteStart, IReadOnlyDictionary<int, LineBreakKind> breaks) {
		ParagraphCluster[] paraClusters = new ParagraphCluster[shapedClusters.Length];
		for (int i = 0; i < shapedClusters.Length; i++) {
			ShapedCluster shapedCluster = shapedClusters[i];
			int absoluteSourceStart = item.SourceStart + shapedCluster.SourceStart;
			int absoluteSourceLimit = absoluteSourceStart + shapedCluster.SourceLength;
			if (!breaks.TryGetValue(absoluteSourceLimit - paraAbsoluteStart, out LineBreakKind breakAfter))
				breakAfter = LineBreakKind.None;
			paraClusters[i] = new ParagraphCluster(
				GlyphStart: shapedCluster.GlyphStart,
				GlyphCount: shapedCluster.GlyphCount,
				SourceStart: absoluteSourceStart,
				SourceLength: shapedCluster.SourceLength,
				Width: shapedCluster.Width,
				IsWhitespaceOnly: shapedCluster.IsWhitespaceOnly,
				BreakAfter: breakAfter
			);
		}
		return paraClusters;
	}

	public static ParagraphCluster[] FlattenSourceOrderClusters(ReadOnlySpan<ParagraphRun> paraRuns) {
		List<ParagraphCluster> flattened = new List<ParagraphCluster>();
		foreach (ParagraphRun paraRun in paraRuns)
			foreach (ParagraphCluster cluster in paraRun.SourceOrderClusters)
				flattened.Add(cluster);
		return flattened.ToArray();
	}

	public static Dictionary<int, LineBreakKind> BuildBreaksDict(LineBreakOpportunity[] brklist) {
		Dictionary<int, LineBreakKind> dict = new Dictionary<int, LineBreakKind>(brklist.Length);
		for (int i = 0; i < brklist.Length; i++)
			dict[brklist[i].Position] = brklist[i].Kind;
		return dict;
	}

	public static LogicalLine[] WrapParaLogicalLines(ReadOnlySpan<ParagraphCluster> paraClusters, int paraAbsoluteStart, int paraLength,
		float maxWidth, TextWrapMode wrapMode) {
		if (paraLength == 0)
			return [new LogicalLine(Start: paraAbsoluteStart, Limit: paraAbsoluteStart)];
		List<LogicalLine> lines = new List<LogicalLine>();
		bool wrap = wrapMode == TextWrapMode.Greedy && !float.IsPositiveInfinity(maxWidth);
		int lineStart = paraAbsoluteStart;
		float lineWidth = 0f;
		int lastBreakSourceLimit = -1;
		int i = 0;
		while (i < paraClusters.Length) {
			ParagraphCluster paraCluster = paraClusters[i];
			float nextWidth = lineWidth + paraCluster.Width;
			if (paraCluster.BreakAfter == LineBreakKind.Soft) {
				lastBreakSourceLimit = paraCluster.SourceLimit;
			} else if (paraCluster.BreakAfter == LineBreakKind.Hard) {
				lines.Add(new LogicalLine(lineStart, paraCluster.SourceLimit));
				lineStart = paraCluster.SourceLimit;
				lineWidth = 0f;
				lastBreakSourceLimit = -1;
				i++;
				continue;
			}

			if (wrap && nextWidth > maxWidth && lineStart < paraCluster.SourceStart) {
				if (lastBreakSourceLimit > lineStart) {
					lines.Add(new LogicalLine(Start: lineStart, Limit: TrimTrailingWs(paraClusters, lineStart, lastBreakSourceLimit)));
					lineStart = SkipLeadingWs(paraClusters, lastBreakSourceLimit);
					lineWidth = ComputeWidth(paraClusters, lineStart, paraCluster.SourceStart);
				} else {
					lines.Add(new LogicalLine(Start: lineStart, Limit: paraCluster.SourceStart));
					lineStart = paraCluster.SourceStart;
					lineWidth = 0f;
				}
				lastBreakSourceLimit = -1;
				continue;
			}
			lineWidth = nextWidth;
			i++;
		}
		lines.Add(new LogicalLine(Start: lineStart, Limit: paraAbsoluteStart + paraLength));
		return lines.ToArray();
	}

	public static int SkipLeadingWs(ReadOnlySpan<ParagraphCluster> paraClusters, int lineStart) {
		int start = lineStart;
		foreach (ParagraphCluster paraCluster in paraClusters) {
			if (paraCluster.SourceStart < lineStart)
				continue;
			if (!paraCluster.IsWhitespaceOnly)
				break;
			start = paraCluster.SourceLimit;
		}
		return start;
	}

	public static int TrimTrailingWs(ReadOnlySpan<ParagraphCluster> paraClusters, int lineStart, int lineLimit) {
		int limit = lineLimit;
		for (int i = paraClusters.Length - 1; i >= 0; i--) {
			ParagraphCluster paraCluster = paraClusters[i];
			if (!Contains(paraCluster, lineStart, lineLimit))
				continue;
			if (!paraCluster.IsWhitespaceOnly)
				break;
			limit = paraCluster.SourceStart;
		}
		return limit;
	}

	public static float ComputeWidth(ReadOnlySpan<ParagraphCluster> paraClusters, int start, int limit) {
		float w = 0f;
		foreach (ParagraphCluster paraCluster in paraClusters)
			if (Contains(paraCluster, start, limit))
				w += paraCluster.Width;
		return w;
	}

	public static float EmitVisualLineGlyphs(ReadOnlySpan<ParagraphRun> paraRuns, string paraText, int paraAbsoluteStart,
		LogicalLine logiLine, float baselineY, List<PlannedGlyph> dst) {
		VisualBidiRun[] visualRuns = TextAnalysis.GetVisualBidiRunsForLine(
			paraText,
			logiLine.Start - paraAbsoluteStart,
			logiLine.Limit - paraAbsoluteStart
		);
		float penX = 0f;
		foreach (VisualBidiRun visualRun in visualRuns) {
			int start = paraAbsoluteStart + visualRun.Start;
			int limit = start + visualRun.Length;
			switch (visualRun.Direction) {
			case Direction.LeftToRight:
				foreach (ParagraphRun paraRun in paraRuns) {
					if (!Overlaps(paraRun, start, limit))
						continue;
					EmitParaRunGlyphs(paraRun, start, limit, baselineY, ref penX, dst);
				}
				break;
			case Direction.RightToLeft:
				for (int i = paraRuns.Length - 1; i >= 0; i--) {
					ParagraphRun paraRun = paraRuns[i];
					if (!Overlaps(paraRun, start, limit))
						continue;
					EmitParaRunGlyphs(paraRun, start, limit, baselineY, ref penX, dst);
				}
				break;
			default:
				throw new InternalStateException($"unexpected direction {visualRun.Direction} in bidi visual run");
			}
		}
		return penX;
	}

	public static void EmitParaRunGlyphs(ParagraphRun run, int sliceStart, int sliceLimit, float baselineY,
		ref float penX, List<PlannedGlyph> dst) {
		foreach (ParagraphCluster paraCluster in run.GlyphOrderClusters) {
			if (!Contains(paraCluster, sliceStart, sliceLimit))
				continue;
			for (int i = paraCluster.GlyphStart; i < paraCluster.GlyphStart + paraCluster.GlyphCount; i++) {
				ShapedGlyph shaped = run.ShapedRun.Glyphs[i];
				dst.Add(new PlannedGlyph(
					Font: run.Font,
					GlyphID: shaped.GlyphID,
					Cluster: shaped.Cluster,
					X: penX + shaped.XOffset,
					Y: baselineY - shaped.YOffset
				));
				penX += shaped.XAdvance;
				// TODO: YAdvance and such
			}
		}
	}

	public static bool Contains(ParagraphCluster paraCluster, int start, int limit) =>
		paraCluster.SourceStart >= start && paraCluster.SourceLimit <= limit;

	public static bool Overlaps(ParagraphRun paraRun, int start, int limit) =>
		paraRun.SourceStart < limit && paraRun.SourceLimit > start;
}

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
