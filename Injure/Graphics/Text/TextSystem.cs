// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using FreeTypeSharp;
using HarfBuzzSharp;
using static FreeTypeSharp.FT;

using Injure.Assets;
using Injure.Rendering;
using System.Diagnostics;

namespace Injure.Graphics.Text;

public readonly record struct TextStyle(
	FontOptions FontOptions,
	Color32 Color,
	float MaxWidth = float.PositiveInfinity,
	TextWrapMode WrapMode = TextWrapMode.None,
	string Locale = "und",
	string? LanguageBCP47 = null
);

public sealed unsafe class TextSystem : IDisposable {
	private readonly record struct LoadedFaceKey(FontSourceKind SourceKind, ulong SourceID, ulong Version, int FaceIndex);

	private readonly FT_LibraryRec_ *ftLibrary;
	private readonly Dictionary<ResolvedFontKey, IResolvedFont> fonts = new Dictionary<ResolvedFontKey, IResolvedFont>();
	private readonly Dictionary<LoadedFaceKey, LoadedFontFace> loadedFaces = new Dictionary<LoadedFaceKey, LoadedFontFace>();

	private readonly ITextItemizer itemizer;
	private readonly ShapeCache shapeCache;
	private readonly FallbackProbeCache fallbackProbeCache;
	private readonly FallbackResolver fallbackResolver;
	private readonly GlyphAtlas atlas;
	private bool disposed = false;

	internal FT_LibraryRec_ *FtLibrary { get { ObjectDisposedException.ThrowIf(disposed, this); return ftLibrary; } }

	internal TextSystem(WebGPURenderer renderer, ITextItemizer? itemizer = null, int atlasPageWidth = 1024, int atlasPageHeight = 1024) {
		fixed (FT_LibraryRec_ **l = &ftLibrary)
			FTException.Check(FT_Init_FreeType(l));
		this.itemizer = itemizer ?? new DefaultTextItemizer();
		shapeCache = new ShapeCache();
		fallbackProbeCache = new FallbackProbeCache();
		fallbackResolver = new FallbackResolver(shapeCache, fallbackProbeCache);
		atlas = new GlyphAtlas(renderer, atlasPageWidth, atlasPageHeight);
	}

	internal IResolvedFont ResolveFont(Font font, int faceIndex, FontOptions opts) {
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
		if (opts.PixelSize <= 0)
			throw new ArgumentOutOfRangeException(nameof(opts), "PixelSize must be > 0");
		ResolvedFontKey key = new ResolvedFontKey(FontSourceKind.Direct, font.ID, faceIndex, opts);
		if (!fonts.TryGetValue(key, out IResolvedFont? fnt)) {
			fnt = new ResolvedDirectFont(this, font, faceIndex, opts);
			fonts.Add(key, fnt);
		}
		return fnt;
	}

	internal IResolvedFont ResolveFont(AssetRef<Font> font, int faceIndex, FontOptions opts) {
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
		if (opts.PixelSize <= 0)
			throw new ArgumentOutOfRangeException(nameof(opts), "PixelSize must be > 0");
		ResolvedFontKey key = new ResolvedFontKey(FontSourceKind.Asset, font.SlotID, faceIndex, opts);
		if (!fonts.TryGetValue(key, out IResolvedFont? fnt)) {
			fnt = new ResolvedAssetSourcedFont(this, font, faceIndex, opts);
			fonts.Add(key, fnt);
		}
		return fnt;
	}

	internal IResolvedFont ResolveFont(FontSpec spec, FontOptions opts) {
		return spec.SourceKind switch {
			FontSourceKind.Direct => ResolveFont(spec.Direct, spec.FaceIndex, opts),
			FontSourceKind.Asset => ResolveFont(spec.Asset, spec.FaceIndex, opts),
			_ => throw new UnreachableException()
		};
	}

	internal ResolvedFontFallbackChain ResolveFallbackChain(FontFallbackChain fonts, FontOptions opts) {
		return new ResolvedFontFallbackChain(
			fonts.ID,
			ResolveFont(fonts.Primary, opts),
			fonts.Fallbacks.Select(f => ResolveFont(f, opts))
		);
	}

	internal LoadedFontFace GetOrCreateLoadedFace(Font font, int faceIndex) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(faceIndex, font.FaceCount);
		LoadedFaceKey key = new LoadedFaceKey(FontSourceKind.Direct, font.ID, ResolvedDirectFont.Version, faceIndex);
		if (!loadedFaces.TryGetValue(key, out LoadedFontFace? face)) {
			face = new LoadedFontFace(font, faceIndex);
			loadedFaces.Add(key, face);
		}
		return face;
	}

	internal LoadedFontFace GetOrCreateLoadedFace(AssetRef<Font> font, AssetLease<Font> lease, int faceIndex) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(faceIndex, lease.Value.FaceCount);
		LoadedFaceKey key = new LoadedFaceKey(FontSourceKind.Asset, font.SlotID, lease.Version, faceIndex);
		if (!loadedFaces.TryGetValue(key, out LoadedFontFace? face)) {
			face = new LoadedFontFace(lease.Value, faceIndex);
			loadedFaces.Add(key, face);
		}
		return face;
	}

	internal TextLayout Layout(ResolvedFontFallbackChain fonts, ReadOnlySpan<char> text, in TextStyle style) {
		ObjectDisposedException.ThrowIf(disposed, this);
		List<TextGlyph> glyphs = new List<TextGlyph>();
		List<TextLine> lines = new List<TextLine>();
		float lineTopY = 0f;
		float maxLineWidth = 0f;
		int paraStart = 0;
		for (int i = 0; i <= text.Length; i++) {
			bool isParaBreak = i == text.Length || text[i] == '\n';
			if (!isParaBreak)
				continue;
			string paraText = new string(text[paraStart..i]);
			ParagraphRun[] paraRuns = buildParaRuns(fonts, paraText, paraStart, style.Locale, style.LanguageBCP47);
			ParagraphCluster[] logiClusters = flattenSourceOrderClusters(paraRuns);
			LogicalLine[] logiLines = wrapParaLogicalLines(logiClusters, paraStart, paraText.Length, style.MaxWidth, style.WrapMode);
			foreach (LogicalLine logiLine in logiLines) {
				FontLineMetrics metrics = computeLineMetrics(paraRuns, logiLine, fonts.Primary.GetState().LineMetrics);
				int glyphStart = glyphs.Count;
				float baselineY = lineTopY + metrics.Ascent;
				float lineW = emitVisualLineGlyphs(paraRuns, paraText, paraStart, logiLine, baselineY, style.Color, glyphs);
				lines.Add(new TextLine(GlyphStart: glyphStart, GlyphCount: glyphs.Count - glyphStart, Width: lineW, BaselineY: baselineY));
				maxLineWidth = Math.Max(lineW, maxLineWidth);
				lineTopY += metrics.Height;
			}
			paraStart = i + 1;
		}
		return new TextLayout {
			Glyphs = glyphs.ToArray(),
			Lines = lines.ToArray(),
			Width = maxLineWidth,
			Height = lineTopY
		};
	}

	public Font LoadFont(byte[] data, string? debugName = null) {
		fixed (byte *p = data) {
			FT_FaceRec_ *probe = null;
			FTException.Check(FT_New_Memory_Face(FtLibrary, p, data.Length, -1, &probe));
			try {
				return new Font(data, debugName, checked((int)probe->num_faces));
			} finally {
				if (probe is not null)
					FT_Done_Face(probe);
			}
		}
	}

	public LiveText Make(FontSpec font, ReadOnlySpan<char> text, TextStyle style) =>
		new LiveText(this, new FontFallbackChain(font), text, style);

	public LiveText Make(FontFallbackChain fonts, ReadOnlySpan<char> text, TextStyle style) =>
		new LiveText(this, fonts, text, style);

	public TextLayout Layout(FontSpec font, ReadOnlySpan<char> text, in TextStyle style) =>
		Layout(ResolveFallbackChain(new FontFallbackChain(font), style.FontOptions), text, in style);

	public TextLayout Layout(FontFallbackChain fonts, ReadOnlySpan<char> text, in TextStyle style) =>
		Layout(ResolveFallbackChain(fonts, style.FontOptions), text, in style);

	private static FontLineMetrics computeLineMetrics(ReadOnlySpan<ParagraphRun> paraRuns, LogicalLine logiLine, FontLineMetrics fallback) {
		float ascent = 0f;
		float descent = 0f;
		float lineGap = 0f;
		bool any = false;
		foreach (ParagraphRun paraRun in paraRuns) {
			if (!overlaps(paraRun, logiLine.Start, logiLine.Limit))
				continue;
			FontLineMetrics m = paraRun.Font.GetState().LineMetrics;
			ascent = Math.Max(ascent, m.Ascent);
			descent = Math.Max(descent, m.Descent);
			lineGap = Math.Max(lineGap, m.LineGap);
			any = true;
		}
		return any ? new FontLineMetrics(ascent, descent, lineGap, Height: ascent + descent + lineGap) : fallback;
	}

	private ParagraphRun[] buildParaRuns(ResolvedFontFallbackChain fonts, string paraText, int paraAbsoluteStart,
		string locale, string? languageBCP47) {
		LineBreakOpportunity[] brklist = TextAnalysis.GetLineBreaks(paraText, locale);
		Dictionary<int, LineBreakKind> breaks = buildBreaksDict(brklist);
		LogicalBidiRun[] bidiRuns = TextAnalysis.GetLogicalBidiRuns(paraText);
		List<ParagraphRun> paraRuns = new List<ParagraphRun>();
		foreach (LogicalBidiRun bidiRun in bidiRuns) {
			ReadOnlySpan<char> bidiRunText = paraText.AsSpan(bidiRun.Start, bidiRun.Length);
			TextItem[] items = itemizer.Itemize(bidiRunText, paraAbsoluteStart + bidiRun.Start, bidiRun.Direction, languageBCP47);
			foreach (TextItem item in items) {
				ResolvedItem[] resolvedItems = fallbackResolver.ResolveItems(fonts, item);
				foreach (ResolvedItem resolved in resolvedItems)
					paraRuns.Add(buildParaRun(resolved.Font, resolved.Item, resolved.ShapedRun, paraAbsoluteStart, breaks));
			}
		}
		return paraRuns.ToArray();
	}

	private static ParagraphRun buildParaRun(IResolvedFont font, in TextItem item, ShapedRun shapedRun, int paraAbsoluteStart, IReadOnlyDictionary<int, LineBreakKind> breaks) {
		ParagraphCluster[] sourceOrderClusters = buildParaClusters(item, shapedRun.SourceOrderClusters, paraAbsoluteStart, breaks);
		ParagraphCluster[] glyphOrderClusters = buildParaClusters(item, shapedRun.GlyphOrderClusters, paraAbsoluteStart, breaks);
		return new ParagraphRun {
			Font = font,
			Item = item,
			ShapedRun = shapedRun,
			SourceOrderClusters = sourceOrderClusters,
			GlyphOrderClusters = glyphOrderClusters,
		};
	}

	private static ParagraphCluster[] buildParaClusters(in TextItem item, ShapedCluster[] shapedClusters,
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

	private static ParagraphCluster[] flattenSourceOrderClusters(ReadOnlySpan<ParagraphRun> paraRuns) {
		List<ParagraphCluster> flattened = new List<ParagraphCluster>();
		foreach (ParagraphRun paraRun in paraRuns)
			foreach (ParagraphCluster cluster in paraRun.SourceOrderClusters)
				flattened.Add(cluster);
		return flattened.ToArray();
	}

	private static Dictionary<int, LineBreakKind> buildBreaksDict(LineBreakOpportunity[] brklist) {
		Dictionary<int, LineBreakKind> dict = new Dictionary<int, LineBreakKind>(brklist.Length);
		for (int i = 0; i < brklist.Length; i++)
			dict[brklist[i].Position] = brklist[i].Kind;
		return dict;
	}

	private static LogicalLine[] wrapParaLogicalLines(ReadOnlySpan<ParagraphCluster> paraClusters, int paraAbsoluteStart, int paraLength,
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
					lines.Add(new LogicalLine(Start: lineStart, Limit: trimTrailingWs(paraClusters, lineStart, lastBreakSourceLimit)));
					lineStart = skipLeadingWs(paraClusters, lastBreakSourceLimit);
					lineWidth = recomputeW(paraClusters, lineStart, paraCluster.SourceStart);
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

	private static int skipLeadingWs(ReadOnlySpan<ParagraphCluster> paraClusters, int lineStart) {
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

	private static int trimTrailingWs(ReadOnlySpan<ParagraphCluster> paraClusters, int lineStart, int lineLimit) {
		int limit = lineLimit;
		for (int i = paraClusters.Length - 1; i >= 0; i--) {
			ParagraphCluster paraCluster = paraClusters[i];
			if (!contains(paraCluster, lineStart, lineLimit))
				continue;
			if (!paraCluster.IsWhitespaceOnly)
				break;
			limit = paraCluster.SourceStart;
		}
		return limit;
	}

	private static float recomputeW(ReadOnlySpan<ParagraphCluster> paraClusters, int start, int limit) {
		float w = 0f;
		foreach (ParagraphCluster paraCluster in paraClusters)
			if (contains(paraCluster, start, limit))
				w += paraCluster.Width;
		return w;
	}

	private float emitVisualLineGlyphs(ReadOnlySpan<ParagraphRun> paraRuns, string paraText, int paraAbsoluteStart,
		LogicalLine logiLine, float baselineY, Color32 color, List<TextGlyph> dst) {
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
					if (!overlaps(paraRun, start, limit))
						continue;
					emitParaRunGlyphs(paraRun, start, limit, baselineY, color, ref penX, dst);
				}
				break;
			case Direction.RightToLeft:
				for (int i = paraRuns.Length - 1; i >= 0; i--) {
					ParagraphRun paraRun = paraRuns[i];
					if (!overlaps(paraRun, start, limit))
						continue;
					emitParaRunGlyphs(paraRun, start, limit, baselineY, color, ref penX, dst);
				}
				break;
			default:
				throw new InternalStateException($"unexpected direction {visualRun.Direction} in bidi visual run");
			}
		}
		return penX;
	}

	private void emitParaRunGlyphs(ParagraphRun run, int sliceStart, int sliceLimit, float baselineY, Color32 color,
		ref float penX, List<TextGlyph> dst) {
		foreach (ParagraphCluster paraCluster in run.GlyphOrderClusters) {
			if (!contains(paraCluster, sliceStart, sliceLimit))
				continue;
			for (int i = paraCluster.GlyphStart; i < paraCluster.GlyphStart + paraCluster.GlyphCount; i++) {
				ShapedGlyph shaped = run.ShapedRun.Glyphs[i];
				if (atlas.TryGetOrCreate(run.Font, shaped.GlyphID, out GlyphAtlasEntry atlasEntry)) {
					float x = penX + shaped.XOffset + atlasEntry.BitmapLeft;
					float y = baselineY - shaped.YOffset - atlasEntry.BitmapTop;
					dst.Add(new TextGlyph(
						Atlas: atlasEntry.Page.Texture,
						SrcPixels: atlasEntry.SrcPixels,
						DstPixels: new RectF(x, y, atlasEntry.Width, atlasEntry.Height),
						Color: color,
						GlyphID: shaped.GlyphID,
						Cluster: shaped.Cluster
					));
				}
				penX += shaped.XAdvance;
			}
		}
	}

	private static bool contains(ParagraphCluster paraCluster, int start, int limit) =>
		paraCluster.SourceStart >= start && paraCluster.SourceLimit <= limit;

	private static bool overlaps(ParagraphRun paraRun, int start, int limit) =>
		paraRun.SourceStart < limit && paraRun.SourceLimit > start;

	public void ClearCache() {
		ObjectDisposedException.ThrowIf(disposed, this);
		shapeCache.Clear();
		fallbackProbeCache.Clear();
	}

	public void ClearGlyphAtlas() {
		ObjectDisposedException.ThrowIf(disposed, this);
		atlas.ClearGlyphs();
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		atlas.Dispose();
	}
}
