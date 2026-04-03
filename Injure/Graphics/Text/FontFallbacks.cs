// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
		// FNV-1a
		const ulong basis = 14695981039346656037ul;
		const ulong prime = 1099511628211ul;
		ulong hash = basis;
		foreach (FontSpec fnt in allFonts) {
			mix32(ref hash, (uint)fnt.SourceKind);
			mix32(ref hash, unchecked((uint)fnt.FaceIndex));
			switch (fnt.SourceKind) {
			case FontSourceKind.Direct:
				mix64(ref hash, fnt.Direct.ID);
				break;
			case FontSourceKind.Asset:
				ulong ver = 0;
				if (fnt.Asset.TryPassiveBorrow(out AssetLease<Font> lease))
					ver = lease.Version;
				mix64(ref hash, fnt.Asset.SlotID);
				mix64(ref hash, ver);
				break;
			default:
				throw new UnreachableException();
			}
		}
		return hash;
		static void mix32(ref ulong h, uint x) {
			h ^= (byte)x;         h *= prime;
			h ^= (byte)(x >> 8);  h *= prime;
			h ^= (byte)(x >> 16); h *= prime;
			h ^= (byte)(x >> 24); h *= prime;
		}
		static void mix64(ref ulong h, ulong x) {
			h ^= (byte)x;         h *= prime;
			h ^= (byte)(x >> 8);  h *= prime;
			h ^= (byte)(x >> 16); h *= prime;
			h ^= (byte)(x >> 24); h *= prime;
			h ^= (byte)(x >> 32); h *= prime;
			h ^= (byte)(x >> 40); h *= prime;
			h ^= (byte)(x >> 48); h *= prime;
			h ^= (byte)(x >> 56); h *= prime;
		}
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
		// FNV-1a
		const ulong basis = 14695981039346656037ul;
		const ulong prime = 1099511628211ul;
		ulong hash = basis;
		foreach (IResolvedFont fnt in allFonts) {
			FontCacheToken t = fnt.GetCacheToken();
			mix32(ref hash, (uint)t.Key.SourceKind);
			mix64(ref hash, t.Key.ID);
			mix32(ref hash, unchecked((uint)t.Key.FaceIndex));
			mix32(ref hash, unchecked((uint)t.Key.Options.PixelSize));
			mix32(ref hash, (uint)t.Key.Options.RasterMode);
			mix32(ref hash, (uint)t.Key.Options.Hinting);
			mix32(ref hash, t.Key.Options.UseEmbeddedBitmaps ? 1u : 0u);
			mix64(ref hash, t.Version);
		}
		return hash;
		static void mix32(ref ulong h, uint x) {
			h ^= (byte)x;         h *= prime;
			h ^= (byte)(x >> 8);  h *= prime;
			h ^= (byte)(x >> 16); h *= prime;
			h ^= (byte)(x >> 24); h *= prime;
		}
		static void mix64(ref ulong h, ulong x) {
			h ^= (byte)x;         h *= prime;
			h ^= (byte)(x >> 8);  h *= prime;
			h ^= (byte)(x >> 16); h *= prime;
			h ^= (byte)(x >> 24); h *= prime;
			h ^= (byte)(x >> 32); h *= prime;
			h ^= (byte)(x >> 40); h *= prime;
			h ^= (byte)(x >> 48); h *= prime;
			h ^= (byte)(x >> 56); h *= prime;
		}
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

