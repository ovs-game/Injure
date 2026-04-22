// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Hashing;
using System.Linq;
using System.Threading;
using HarfBuzzSharp;

using Injure.Assets;

namespace Injure.Graphics.Text;

public sealed class FontFallbackChain {
	private static int nextID = 0;
	private readonly FontSpec[] allFonts;

	public int ID { get; } = Interlocked.Increment(ref nextID);
	public FontSpec Primary { get; }
	public IReadOnlyList<FontSpec> Fallbacks { get; }
	public ReadOnlySpan<FontSpec> AllFonts => allFonts;

	public FontFallbackChain(FontSpec primary, params FontSpec[] fallbacks) {
		Primary = primary;
		Fallbacks = fallbacks ?? Array.Empty<FontSpec>();
		allFonts = new FontSpec[Fallbacks.Count + 1];
		allFonts[0] = Primary;
		for (int i = 0; i < Fallbacks.Count; i++)
			allFonts[i + 1] = Fallbacks[i];
	}

	internal ulong Hash() {
		XxHash3 h = new XxHash3();
		const int estimated = 4 + 4 + 8 + 8; // worst-case
		Span<byte> buf = stackalloc byte[estimated];
		foreach (FontSpec fnt in allFonts) {
			int i = 0;
			BinaryPrimitives.WriteUInt32LittleEndian(buf[i..], (uint)fnt.SourceKind); i += 4;
			BinaryPrimitives.WriteUInt32LittleEndian(buf[i..], unchecked((uint)fnt.FaceIndex)); i += 4;
			switch (fnt.SourceKind) {
			case FontSourceKind.Direct:
				BinaryPrimitives.WriteUInt64LittleEndian(buf[i..], fnt.Direct.ID); i += 8;
				break;
			case FontSourceKind.Asset:
				ulong ver = 0;
				if (fnt.Asset.TryPassiveBorrow(out AssetLease<Font> lease))
					ver = lease.Version;
				BinaryPrimitives.WriteUInt64LittleEndian(buf[i..], fnt.Asset.SlotID); i += 8;
				BinaryPrimitives.WriteUInt64LittleEndian(buf[i..], ver); i += 8;
				break;
			default:
				throw new UnreachableException();
			}
			h.Append(buf);
		}
		return h.GetCurrentHashAsUInt64();
	}

}

internal sealed class ResolvedFontFallbackChain {
	private readonly IResolvedFont[] allFonts;

	public int ID { get; }
	public IResolvedFont Primary { get; }
	public IReadOnlyList<IResolvedFont> Fallbacks { get; }
	public ReadOnlySpan<IResolvedFont> AllFonts => allFonts;

	public ResolvedFontFallbackChain(int id, IResolvedFont primary, IEnumerable<IResolvedFont>? fallbacks) {
		ID = id;
		Primary = primary;
		Fallbacks = fallbacks?.ToArray() ?? Array.Empty<IResolvedFont>();
		allFonts = new IResolvedFont[Fallbacks.Count + 1];
		allFonts[0] = Primary;
		for (int i = 0; i < Fallbacks.Count; i++)
			allFonts[i + 1] = Fallbacks[i];
	}

	public ulong Hash() {
		XxHash3 h = new XxHash3();
		const int estimated = 4 + 8 + 4 + 4 + 4 + 4 + 1 + 8;
		Span<byte> buf = stackalloc byte[estimated];
		foreach (IResolvedFont fnt in allFonts) {
			FontCacheToken t = fnt.GetCacheToken();
			int i = 0;
			BinaryPrimitives.WriteUInt32LittleEndian(buf[i..], (uint)t.Key.SourceKind); i += 4;
			BinaryPrimitives.WriteUInt64LittleEndian(buf[i..], t.Key.ID); i += 8;
			BinaryPrimitives.WriteUInt32LittleEndian(buf[i..], unchecked((uint)t.Key.FaceIndex)); i += 4;
			BinaryPrimitives.WriteUInt32LittleEndian(buf[i..], unchecked((uint)t.Key.Options.PixelSize)); i += 4;
			BinaryPrimitives.WriteUInt32LittleEndian(buf[i..], (uint)t.Key.Options.RasterMode.Tag); i += 4;
			BinaryPrimitives.WriteUInt32LittleEndian(buf[i..], (uint)t.Key.Options.Hinting.Tag); i += 4;
			buf[i++] = t.Key.Options.UseEmbeddedBitmaps ? (byte)1 : (byte)0;
			BinaryPrimitives.WriteUInt64LittleEndian(buf[i..], t.Version); i += 8;
			h.Append(buf);
		}
		return h.GetCurrentHashAsUInt64();
	}
}

internal readonly record struct FallbackProbeKey(
	int FallbackChainID,
	ulong FallbackChainHash,
	string Text,
	Direction Direction,
	Script? Script,
	string? LanguageBCP47
);

internal sealed class FallbackProbeCache(TextSystem text, int maxEntries, int maxEstimatedCost) {
	private sealed class Entry {
		public required int FontIndex;
		public required ulong LastUseStamp;
		public required int EstimatedCost;
	}

	private readonly TextSystem text = text;
	private readonly int maxEntries = maxEntries;
	private readonly int maxEstimatedCost = maxEstimatedCost;
	private readonly Dictionary<FallbackProbeKey, Entry> cache = new Dictionary<FallbackProbeKey, Entry>();
	private ulong nextUseStamp = 0; // first will be 1 since this gets incremented upfront
	private int totalEstimatedCost = 0;

	public void Clear() {
		cache.Clear();
		totalEstimatedCost = 0;
	}

	public bool TryGet(FallbackProbeKey key, out int fontIndex) {
		if (cache.TryGetValue(key, out Entry? ent)) {
			ent.LastUseStamp = ++nextUseStamp;
			fontIndex = ent.FontIndex;
			return true;
		}
		fontIndex = default;
		return false;
	}

	public void Set(FallbackProbeKey key, int fontIndex) {
		if (cache.Remove(key, out Entry? ent))
			totalEstimatedCost -= ent.EstimatedCost;
		int est = estimate(key);
		cache[key] = new Entry {
			FontIndex = fontIndex,
			LastUseStamp = ++nextUseStamp,
			EstimatedCost = est
		};
		totalEstimatedCost += est;
		text.OnCacheActivity();
		Trim();
	}

	public void Trim() {
		if (cache.Count <= maxEntries && totalEstimatedCost <= maxEstimatedCost)
			return;
		foreach (FallbackProbeKey key in cache
			.OrderBy(static kvp => kvp.Value.LastUseStamp)
			.ThenByDescending(static kvp => kvp.Value.EstimatedCost)
			.Select(static kvp => kvp.Key)) {
			if (cache.Count <= maxEntries && totalEstimatedCost <= maxEstimatedCost)
				break;
			totalEstimatedCost -= cache[key].EstimatedCost;
			cache.Remove(key);
		}
	}

	private static int estimate(FallbackProbeKey key) {
		int cost = 0;
		cost += key.Text.Length * sizeof(char);

		cost += 32; // extra weight for object/etc overhead to avoid pretending small entries are free
		return cost;
	}
}

internal readonly record struct ResolvedItem(
	IResolvedFont Font,
	TextItem Item,
	ShapedRun ShapedRun
);

internal sealed class FallbackResolver(ShapeCache shapeCache, FallbackProbeCache probeCache) {
	private readonly ShapeCache shapeCache = shapeCache;
	private readonly FallbackProbeCache probeCache = probeCache;

	public ResolvedItem[] ResolveItems(ResolvedFontFallbackChain fonts, TextItem item) {
		GraphemeSpan[] graphemes = TextAnalysis.GetGraphemeSpans(item.Text);
		if (graphemes.Length == 0)
			return Array.Empty<ResolvedItem>();
		List<(int Start, int Limit, int FontIndex)> spans = new List<(int Start, int Limit, int FontIndex)>();
		GraphemeSpan first = graphemes[0];
		int currStart = first.Start;
		int currLimit = first.Start + first.Length;
		int currFontIdx = resolveFontIndex(fonts, item, first);
		for (int i = 1; i < graphemes.Length; i++) {
			GraphemeSpan grapheme = graphemes[i];
			int fontIndex = resolveFontIndex(fonts, item, grapheme);
			if (fontIndex != currFontIdx) {
				spans.Add((currStart, currLimit, currFontIdx));
				currStart = grapheme.Start;
				currLimit = grapheme.Start + grapheme.Length;
				currFontIdx = fontIndex;
			} else {
				currLimit = grapheme.Start + grapheme.Length;
			}
		}
		spans.Add((currStart, currLimit, currFontIdx));
		ResolvedItem[] resolved = new ResolvedItem[spans.Count];
		for (int i = 0; i < spans.Count; i++) {
			(int start, int limit, int fontIndex) = spans[i];
			TextItem mergedItem = item with {
				SourceStart = item.SourceStart + start,
				Text = item.Text[start..limit]
			};
			IResolvedFont font = fonts.AllFonts[fontIndex];
			ShapedRun shapedRun = shapeCache.GetOrCreate(font, mergedItem);
			resolved[i] = new ResolvedItem(
				Font: font,
				Item: mergedItem,
				ShapedRun: shapedRun
			);
		}
		return resolved;
	}

	private int resolveFontIndex(ResolvedFontFallbackChain fonts, TextItem parentItem, GraphemeSpan grapheme) {
		FallbackProbeKey key = new FallbackProbeKey(
			FallbackChainID: fonts.ID,
			FallbackChainHash: fonts.Hash(),
			Text: parentItem.Text.Substring(grapheme.Start, grapheme.Length),
			Direction: parentItem.Properties.Direction ?? throw new InternalStateException("fallback probing requires an explicit direction"),
			Script: parentItem.Properties.Script,
			LanguageBCP47: parentItem.Properties.LanguageBCP47
		);
		if (probeCache.TryGet(key, out int fontidx))
			return fontidx;
		TextItem graphemeItem = parentItem with {
			SourceStart = parentItem.SourceStart + grapheme.Start,
			Text = parentItem.Text.Substring(grapheme.Start, grapheme.Length)
		};
		for (int i = 0; i < fonts.AllFonts.Length; i++) {
			if (noNotdefs(shapeCache.GetOrCreate(fonts.AllFonts[i], graphemeItem))) {
				probeCache.Set(key, i);
				return i;
			}
		}
		probeCache.Set(key, 0);
		return 0;
	}

	private static bool noNotdefs(ShapedRun shaped) {
		foreach (ShapedGlyph glyph in shaped.Glyphs)
			if (glyph.GlyphID == 0)
				return false;
		return true;
	}
}

