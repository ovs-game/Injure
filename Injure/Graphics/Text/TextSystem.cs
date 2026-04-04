// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using FreeTypeSharp;
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

public sealed class TextCacheOptions {
	public int MaxShapeEntries { get; init; } = 4096;
	public int MaxShapeEstimatedCost { get; init; } = 8 * 1024 * 1024;

	public int MaxFallbackProbeEntries { get; init; } = 4096;
	public int MaxFallbackProbeEstimatedCost { get; init; } = 1 * 1024 * 1024;

	public int GlyphAtlasPageWidth { get; init; } = 1024;
	public int GlyphAtlasPageHeight { get; init; } = 1024;
	public int GlyphAtlasPadding { get; init; } = 1;
	public int MaxGlyphAtlasPages { get; init; } = 16;

	public int TrimEveryNOperations { get; init; } = 128;
}

public sealed unsafe class TextSystem : IDisposable {
	private readonly record struct LoadedFaceKey(FontSourceKind SourceKind, ulong SourceID, ulong Version, int FaceIndex);

	private readonly FT_LibraryRec_ *ftLibrary;
	private readonly Dictionary<ResolvedFontKey, IResolvedFont> fonts = new Dictionary<ResolvedFontKey, IResolvedFont>();
	private readonly Dictionary<LoadedFaceKey, LoadedFontFace> loadedFaces = new Dictionary<LoadedFaceKey, LoadedFontFace>();

	private readonly ITextItemizer itemizer;
	private readonly TextCacheOptions cacheOptions;
	private readonly ShapeCache shapeCache;
	private readonly FallbackProbeCache fallbackProbeCache;
	private readonly FallbackResolver fallbackResolver;
	private readonly GlyphAtlas atlas;
	private int opCounter = 0;
	private bool disposed = false;

	internal FT_LibraryRec_ *FtLibrary { get { ObjectDisposedException.ThrowIf(disposed, this); return ftLibrary; } }

	internal TextSystem(WebGPURenderer renderer, ITextItemizer? itemizer = null, TextCacheOptions? cacheOptions = null) {
		fixed (FT_LibraryRec_ **l = &ftLibrary)
			FTException.Check(FT_Init_FreeType(l));
		this.itemizer = itemizer ?? new DefaultTextItemizer();
		this.cacheOptions = cacheOptions ?? new TextCacheOptions();
		shapeCache = new ShapeCache(this, this.cacheOptions.MaxShapeEntries, this.cacheOptions.MaxShapeEstimatedCost);
		fallbackProbeCache = new FallbackProbeCache(this, this.cacheOptions.MaxFallbackProbeEntries, this.cacheOptions.MaxFallbackProbeEstimatedCost);
		fallbackResolver = new FallbackResolver(shapeCache, fallbackProbeCache);
		atlas = new GlyphAtlas(
			renderer,
			this,
			this.cacheOptions.GlyphAtlasPageWidth,
			this.cacheOptions.GlyphAtlasPageHeight,
			this.cacheOptions.GlyphAtlasPadding,
			this.cacheOptions.MaxGlyphAtlasPages
		);
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
		List<PlannedGlyph> scratch = new List<PlannedGlyph>();
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
			ParagraphRun[] paraRuns = TextLayouter.BuildParaRuns(itemizer, fallbackResolver, fonts, paraText, paraStart, style.Locale, style.LanguageBCP47);
			ParagraphCluster[] logiClusters = TextLayouter.FlattenSourceOrderClusters(paraRuns);
			LogicalLine[] logiLines = TextLayouter.WrapParaLogicalLines(logiClusters, paraStart, paraText.Length, style.MaxWidth, style.WrapMode);
			foreach (LogicalLine logiLine in logiLines) {
				FontLineMetrics metrics = TextLayouter.ComputeLineMetrics(paraRuns, logiLine, fonts.Primary.GetState().LineMetrics);
				int glyphStart = glyphs.Count;
				float baselineY = lineTopY + metrics.Ascent;
				float lineW = emitMaterializedGlyphs(paraRuns, paraText, paraStart, logiLine, baselineY, style.Color, scratch, glyphs);
				lines.Add(new TextLine(GlyphStart: glyphStart, GlyphCount: glyphs.Count - glyphStart, Width: lineW, BaselineY: baselineY));
				maxLineWidth = Math.Max(lineW, maxLineWidth);
				lineTopY += metrics.Height;
			}
			paraStart = i + 1;
		}

		return new TextLayout(
			glyphs: glyphs.ToArray(),
			lines: lines.ToArray(),
			width: maxLineWidth,
			height: lineTopY,
			retainedPages: glyphs.Select(static g => g.Page).Distinct().ToArray()
		);
	}

	private float emitMaterializedGlyphs(ReadOnlySpan<ParagraphRun> paraRuns, string paraText, int paraAbsoluteStart,
		LogicalLine logiLine, float baselineY, Color32 color, List<PlannedGlyph> scratch, List<TextGlyph> dst) {
		scratch.Clear();
		float lineW = TextLayouter.EmitVisualLineGlyphs(paraRuns, paraText, paraAbsoluteStart, logiLine, baselineY, scratch);
		foreach (PlannedGlyph planned in scratch) {
			if (!atlas.TryGetOrCreate(planned.Font, planned.GlyphID, out GlyphAtlasEntry atlasEntry))
				continue;
			float x = planned.X + atlasEntry.BitmapLeft;
			float y = planned.Y - atlasEntry.BitmapTop;
			dst.Add(new TextGlyph(
				Page: atlasEntry.Page,
				SrcPixels: atlasEntry.SrcPixels,
				DstPixels: new RectF(x, y, atlasEntry.Width, atlasEntry.Height),
				Color: color,
				GlyphID: planned.GlyphID,
				Cluster: planned.Cluster
			));
		}
		return lineW;
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

	internal void OnCacheActivity() {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (++opCounter < cacheOptions.TrimEveryNOperations)
			return;
		opCounter = 0;
		TrimCache();
	}

	public void ClearCache() {
		ObjectDisposedException.ThrowIf(disposed, this);
		shapeCache.Clear();
		fallbackProbeCache.Clear();
		atlas.Clear();
	}

	public void TrimCache() {
		ObjectDisposedException.ThrowIf(disposed, this);
		shapeCache.Trim();
		fallbackProbeCache.Trim();
		atlas.Trim();
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		shapeCache.Dispose();
		atlas.Dispose();
	}
}
