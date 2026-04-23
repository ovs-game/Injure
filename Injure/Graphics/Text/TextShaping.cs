// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarfBuzzSharp;

namespace Injure.Graphics.Text;

internal readonly record struct TextSegmentProperties(
	Direction? Direction = null,
	Script? Script = null,
	string? LanguageBCP47 = null
);

internal readonly record struct TextItem(
	int SourceStart,
	string Text,
	TextSegmentProperties Properties,
	bool GuessSegmentProperties = false
);

internal readonly record struct ShapedGlyph(
	uint GlyphID,
	uint Cluster,
	float XOffset,
	float YOffset,
	float XAdvance,
	float YAdvance
) {
	public static readonly int Size = Unsafe.SizeOf<ShapedGlyph>();
}

internal readonly record struct ShapedCluster(
	int GlyphStart,
	int GlyphCount,
	int SourceStart,
	int SourceLength,
	float Width,
	bool IsWhitespaceOnly) {
	public static readonly int Size = Unsafe.SizeOf<ShapedCluster>();
	public int SourceLimit => SourceStart + SourceLength;
}

internal sealed class ShapedRun {
	public required string Text { get; init; }
	public required TextSegmentProperties Properties { get; init; }
	public required bool GuessSegmentProperties { get; init; }
	public required ShapedGlyph[] Glyphs { get; init; }
	public required ShapedCluster[] SourceOrderClusters { get; init; }
	public required ShapedCluster[] GlyphOrderClusters { get; init; }
	public required float Width { get; init; }
}

internal interface ITextItemizer {
	TextItem[] Itemize(ReadOnlySpan<char> text, int sourceStart, Direction direction, string? languageBCP47);
}

internal sealed class DefaultTextItemizer : ITextItemizer {
	public TextItem[] Itemize(ReadOnlySpan<char> text, int sourceStart, Direction direction, string? languageBCP47) =>
		TextAnalysis.ItemizeByScript(text, sourceStart, direction, languageBCP47);
}

internal sealed class ShapeCache(TextSystem text, int maxEntries, int maxEstimatedCost) : IDisposable {
	private readonly record struct Key(
		FontCacheToken FontCacheToken,
		string Text,
		TextSegmentProperties Properties,
		bool GuessSegmentProperties
	);
	private sealed class Entry {
		public required ShapedRun Shaped;
		public required ulong LastUseStamp;
		public required int EstimatedCost;
	}

	private readonly TextSystem text = text;
	private readonly int maxEntries = maxEntries;
	private readonly int maxEstimatedCost = maxEstimatedCost;
	private readonly Dictionary<Key, Entry> cache = new();
	private readonly Dictionary<string, Language> langs = new(StringComparer.OrdinalIgnoreCase);
	private ulong nextUseStamp = 0; // first will be 1 since this gets incremented upfront
	private int totalEstimatedCost = 0;

	public void Clear() => cache.Clear();

	public ShapedRun GetOrCreate(IResolvedFont font, in TextItem item) {
		Key key = new(
			FontCacheToken: font.GetCacheToken(),
			Text: item.Text,
			Properties: item.Properties,
			GuessSegmentProperties: item.GuessSegmentProperties
		);
		if (cache.TryGetValue(key, out Entry? ent)) {
			ent.LastUseStamp = ++nextUseStamp;
			return ent.Shaped;
		}
		ShapedRun shaped = shape(font, in item);
		int est = estimate(shaped, in item);
		cache.Add(key, new Entry {
			Shaped = shaped,
			LastUseStamp = ++nextUseStamp,
			EstimatedCost = est
		});
		totalEstimatedCost += est;
		text.OnCacheActivity();
		Trim();
		return shaped;
	}

	private ShapedRun shape(IResolvedFont font, in TextItem item) {
		ResolvedFontState st = font.GetState();
		using HarfBuzzSharp.Buffer buf = new();
		buf.AddUtf16(item.Text.AsSpan());
		if (item.GuessSegmentProperties) {
			buf.GuessSegmentProperties();
		} else {
			if (item.Properties.Direction is Direction d)
				buf.Direction = d;
			if (item.Properties.Script is Script s)
				buf.Script = s;
			if (!string.IsNullOrWhiteSpace(item.Properties.LanguageBCP47)) {
				if (!langs.TryGetValue(item.Properties.LanguageBCP47, out Language? l)) {
					l = new Language(item.Properties.LanguageBCP47);
					langs.Add(item.Properties.LanguageBCP47, l);
				}
				buf.Language = l;
			}
		}
		st.HbFont.Shape(buf);
		GlyphInfo[] infos = buf.GlyphInfos;
		GlyphPosition[] positions = buf.GlyphPositions;
		ShapedGlyph[] glyphs = new ShapedGlyph[buf.Length];
		float w = 0f;
		for (int i = 0; i < buf.Length; i++) {
			glyphs[i] = new ShapedGlyph(
				GlyphID: infos[i].Codepoint,
				Cluster: infos[i].Cluster,
				XOffset: positions[i].XOffset / 64.0f,
				YOffset: positions[i].YOffset / 64.0f,
				XAdvance: positions[i].XAdvance / 64.0f,
				YAdvance: positions[i].YAdvance / 64.0f
			);
			w += positions[i].XAdvance / 64.0f;
		}
		(TextSegmentProperties properties, ShapedCluster[] sourceOrderClusters, ShapedCluster[] glyphOrderClusters) =
			buildClusters(item, buf, glyphs);
		return new ShapedRun {
			Text = item.Text,
			Properties = properties,
			GuessSegmentProperties = item.GuessSegmentProperties,
			Glyphs = glyphs,
			SourceOrderClusters = sourceOrderClusters,
			GlyphOrderClusters = glyphOrderClusters,
			Width = w
		};
	}

	private static (TextSegmentProperties Properties, ShapedCluster[] SourceOrderClusters, ShapedCluster[] GlyphOrderClusters) buildClusters(
		in TextItem item, HarfBuzzSharp.Buffer buf, ShapedGlyph[] glyphs
	) {
		List<ShapedCluster> glyphOrderClusters = new();
		if (glyphs.Length != 0) {
			HashSet<uint> uniqueClusters = new();
			for (int i = 0; i < glyphs.Length; i++)
				uniqueClusters.Add(glyphs[i].Cluster);
			uint[] clusterStarts = uniqueClusters.ToArray();
			Array.Sort(clusterStarts);

			Dictionary<uint, int> nextClusterMap = new(clusterStarts.Length);
			for (int i = 0; i < clusterStarts.Length; i++) {
				int next = i + 1 < clusterStarts.Length ? checked((int)clusterStarts[i + 1]) : item.Text.Length;
				nextClusterMap.Add(clusterStarts[i], next);
			}

			int glyphStart = 0;
			while (glyphStart < glyphs.Length) {
				uint cluster = glyphs[glyphStart].Cluster;
				float clusterWidth = glyphs[glyphStart].XAdvance;
				int glyphCount = 1;
				for (; glyphStart + glyphCount < glyphs.Length && glyphs[glyphStart + glyphCount].Cluster == cluster; glyphCount++)
					clusterWidth += glyphs[glyphStart + glyphCount].XAdvance;
				int sourceStart = Math.Clamp(checked((int)cluster), 0, item.Text.Length);
				int sourceLimit = Math.Max(Math.Clamp(nextClusterMap[cluster], 0, item.Text.Length), sourceStart);
				ReadOnlySpan<char> sourceText = item.Text.AsSpan(sourceStart, sourceLimit - sourceStart);
				glyphOrderClusters.Add(new ShapedCluster(
					GlyphStart: glyphStart,
					GlyphCount: glyphCount,
					SourceStart: sourceStart,
					SourceLength: sourceLimit - sourceStart,
					Width: clusterWidth,
					IsWhitespaceOnly: isWsOnly(sourceText))
				);
				glyphStart += glyphCount;
			}
		}
		ShapedCluster[] glyphOrderArray = glyphOrderClusters.ToArray();
		ShapedCluster[] sourceOrderArray = new ShapedCluster[glyphOrderArray.Length];
		Array.Copy(glyphOrderArray, sourceOrderArray, glyphOrderArray.Length);
		Array.Sort(sourceOrderArray, static (ShapedCluster a, ShapedCluster b) => {
			int n = a.SourceStart.CompareTo(b.SourceStart);
			return (n != 0) ? n : a.GlyphStart.CompareTo(b.GlyphStart);
		});
		TextSegmentProperties props = (!item.GuessSegmentProperties) ? item.Properties :
			new TextSegmentProperties(Direction: buf.Direction, Script: buf.Script, LanguageBCP47: item.Properties.LanguageBCP47);
		return (props, sourceOrderArray, glyphOrderArray);
	}

	private static bool isWsOnly(ReadOnlySpan<char> text) {
		if (text.IsEmpty)
			return false;
		for (int i = 0; i < text.Length; i++)
			if (!char.IsWhiteSpace(text[i]))
				return false;
		return true;
	}

	private static int estimate(ShapedRun shaped, in TextItem item) {
		int cost = 0;
		cost += item.Text.Length * sizeof(char);
		cost += shaped.Glyphs.Length * ShapedGlyph.Size;
		cost += shaped.SourceOrderClusters.Length * ShapedCluster.Size;
		cost += shaped.GlyphOrderClusters.Length * ShapedCluster.Size;

		cost += 64; // extra weight for object/etc overhead to avoid pretending small entries are free
		return cost;
	}

	public void Trim() {
		if (cache.Count <= maxEntries && totalEstimatedCost <= maxEstimatedCost)
			return;
		foreach (Key key in cache
			.OrderBy(static kvp => kvp.Value.LastUseStamp)
			.ThenByDescending(static kvp => kvp.Value.EstimatedCost)
			.Select(static kvp => kvp.Key)) {
			if (cache.Count <= maxEntries && totalEstimatedCost <= maxEstimatedCost)
				break;
			totalEstimatedCost -= cache[key].EstimatedCost;
			cache.Remove(key);
		}
	}

	public void Dispose() {
		foreach (Language lang in langs.Values)
			lang.Dispose();
		langs.Clear();
		cache.Clear();
	}
}
