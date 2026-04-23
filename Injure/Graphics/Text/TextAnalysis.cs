// SPDX-License-Identifier: MIT

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using HarfBuzzSharp;

using Injure.Native;
using static Injure.Graphics.Text.FriBidiBindings;

namespace Injure.Graphics.Text;

internal static unsafe partial class FriBidiBindings {
	[LibraryImport("fribidi")]
	public static partial void fribidi_get_bidi_types(uint *str, int len, uint *btypes);

	[LibraryImport("fribidi")]
	public static partial uint fribidi_get_par_direction(uint *bidiTypes, int len);

	[LibraryImport("fribidi")]
	public static partial sbyte fribidi_get_par_embedding_levels_ex(uint *bidiTypes, uint *bracketTypes, int len, uint *pbaseDir, sbyte *embeddingLevels);

	[LibraryImport("fribidi")]
	public static partial sbyte fribidi_reorder_line(uint flags, uint *bidiTypes, int len, int off, uint baseDir, sbyte *embeddingLevels, uint *visualStr, int *map);

	[LibraryImport("fribidi")]
	public static partial uint fribidi_get_bracket(uint ch);
}

internal readonly record struct GraphemeSpan(
	int Start,
	int Length
);

internal static unsafe class TextAnalysis {
	private sealed class ParagraphAnalysis : IDisposable {
		private static readonly ArrayPool<int> intPool = ArrayPool<int>.Shared;
		private static readonly ArrayPool<uint> uintPool = ArrayPool<uint>.Shared;
		private static readonly ArrayPool<sbyte> sbytePool = ArrayPool<sbyte>.Shared;
		private bool disposed = false;

		public string Text { get; }
		public int ScalarLength { get; }
		public int[] ScalarStartsUtf16 { get; }
		public uint[] Scalars { get; }
		public uint[] BidiTypes { get; }
		public uint[] BracketTypes { get; }
		public sbyte[] Levels { get; }
		public uint BaseDir { get; }

		private ParagraphAnalysis(string text, int scalarLength, int[] scalarStartsUtf16, uint[] scalars, uint[] bidiTypes,
			uint[] bracketTypes, sbyte[] levels, uint baseDir)		{
			Text = text;
			ScalarLength = scalarLength;
			ScalarStartsUtf16 = scalarStartsUtf16;
			Scalars = scalars;
			BidiTypes = bidiTypes;
			BracketTypes = bracketTypes;
			Levels = levels;
			BaseDir = baseDir;
		}

		public static ParagraphAnalysis Create(string text) {
			int estimatedScalars = text.Length; // XXX this is an upper-bound estimate that may be too blunt
			int[] scalarStartsUtf16 = intPool.Rent(estimatedScalars + 1);
			uint[] scalars = uintPool.Rent(estimatedScalars);
			uint[] bidiTypes = uintPool.Rent(estimatedScalars);
			uint[] bracketTypes = uintPool.Rent(estimatedScalars);
			sbyte[] levels = sbytePool.Rent(estimatedScalars);
			int scalarCount = 0;
			int utf16Index = 0;
			scalarStartsUtf16[0] = 0;

			while (utf16Index < text.Length) {
				if (Rune.DecodeFromUtf16(text.AsSpan(utf16Index), out Rune rune, out int consumed) != OperationStatus.Done)
					throw new InvalidOperationException($"invalid utf-16 at index {utf16Index}");
				scalars[scalarCount] = (uint)rune.Value;
				utf16Index += consumed;
				scalarCount++;
				scalarStartsUtf16[scalarCount] = utf16Index;
			}

			uint baseDir;
			fixed (uint* pScalars = scalars)
			fixed (uint* pBidiTypes = bidiTypes)
			fixed (uint* pBracketTypes = bracketTypes)
			fixed (sbyte* pLevels = levels) {
				fribidi_get_bidi_types(pScalars, scalarCount, pBidiTypes);
				for (int i = 0; i < scalarCount; i++)
					pBracketTypes[i] = fribidi_get_bracket(pScalars[i]);
				baseDir = fribidi_get_par_direction(pBidiTypes, scalarCount);
				if (fribidi_get_par_embedding_levels_ex(pBidiTypes, pBracketTypes, scalarCount, &baseDir, pLevels) == 0)
					throw new InvalidOperationException("fribidi_get_par_embedding_levels_ex failed");
			}
			return new ParagraphAnalysis(text, scalarCount, scalarStartsUtf16, scalars, bidiTypes, bracketTypes, levels, baseDir);
		}

		public void GetLogicalRuns(List<LogicalBidiRun> dst) {
			ObjectDisposedException.ThrowIf(disposed, this);
			int scalarStart = 0;
			while (scalarStart < ScalarLength) {
				bool rtl = isRtlLevel(Levels[scalarStart]);
				int scalarLimit = scalarStart + 1;
				while (scalarLimit < ScalarLength && isRtlLevel(Levels[scalarLimit]) == rtl)
					scalarLimit++;
				int utf16Start = ScalarStartsUtf16[scalarStart];
				int utf16Limit = ScalarStartsUtf16[scalarLimit];
				dst.Add(new LogicalBidiRun(
					Start: utf16Start,
					Length: utf16Limit - utf16Start,
					Direction: rtl ? Direction.RightToLeft : Direction.LeftToRight
				));
				scalarStart = scalarLimit;
			}
		}

		public void GetVisualRunsForLine(List<VisualBidiRun> dst, int lineStartUtf16, int lineLimitUtf16) {
			ObjectDisposedException.ThrowIf(disposed, this);
			int lineScalarStart = utf16ToScalarBoundary(lineStartUtf16);
			int lineScalarLimit = utf16ToScalarBoundary(lineLimitUtf16);
			int lineScalarLength = lineScalarLimit - lineScalarStart;
			if (lineScalarLength <= 0)
				return;

			int[] visualToLogical = intPool.Rent(lineScalarLength);
			sbyte[] lineLevels = sbytePool.Rent(ScalarLength);
			try {
				Array.Copy(Levels, 0, lineLevels, 0, ScalarLength);
				for (int i = 0; i < lineScalarLength; i++)
					visualToLogical[i] = lineScalarStart + i;

				fixed (uint* pBidiTypes = BidiTypes)
				fixed (sbyte* pLevels = lineLevels)
				fixed (int* pMap = visualToLogical) {
					if (fribidi_reorder_line(FRIBIDI_FLAGS, pBidiTypes, lineScalarLength, lineScalarStart,
						BaseDir, pLevels, null, pMap) == 0)
						throw new InvalidOperationException("fribidi_reorder_line failed");
				}

				int visualIndex = 0;
				while (visualIndex < lineScalarLength) {
					int firstLogical = visualToLogical[visualIndex];
					bool rtl = isRtlLevel(lineLevels[firstLogical]);
					int delta = rtl ? -1 : 1;
					int minLogical = firstLogical;
					int maxLogical = firstLogical;
					int prevLogical = firstLogical;
					int nextVisual = visualIndex + 1;
					while (nextVisual < lineScalarLength) {
						int logical = visualToLogical[nextVisual];
						if (isRtlLevel(lineLevels[logical]) != rtl)
							break;
						if (logical != prevLogical + delta)
							break;
						prevLogical = logical;
						minLogical = Math.Min(minLogical, logical);
						maxLogical = Math.Max(maxLogical, logical);
						nextVisual++;
					}
					int utf16Start = ScalarStartsUtf16[minLogical];
					int utf16Limit = ScalarStartsUtf16[maxLogical + 1];
					dst.Add(new VisualBidiRun(
						Start: utf16Start,
						Length: utf16Limit - utf16Start,
						Direction: rtl ? Direction.RightToLeft : Direction.LeftToRight
					));
					visualIndex = nextVisual;
				}
			} finally {
				intPool.Return(visualToLogical, clearArray: false);
				sbytePool.Return(lineLevels, clearArray: false);
			}
		}

		private int utf16ToScalarBoundary(int utf16Offset) {
			int idx = Array.BinarySearch(ScalarStartsUtf16, 0, ScalarLength + 1, utf16Offset);
			if (idx < 0)
				throw new ArgumentOutOfRangeException(nameof(utf16Offset), $"offset {utf16Offset} is not on a scalar boundary");
			return idx;
		}

		public void Dispose() {
			if (disposed)
				return;
			disposed = true;
			intPool.Return(ScalarStartsUtf16, clearArray: false);
			uintPool.Return(Scalars, clearArray: false);
			uintPool.Return(BidiTypes, clearArray: false);
			uintPool.Return(BracketTypes, clearArray: false);
			sbytePool.Return(Levels, clearArray: false);
		}
	}

	private static readonly UnicodeFunctions hbUnicode = UnicodeFunctions.Default;

	// TODO: a way to pass FRIBIDI_FLAG_REORDER_NSM for rtl combining mark placement
	private const uint FRIBIDI_FLAGS = 0;

	public static GraphemeSpan[] GetGraphemeSpans(string text) {
		if (text.Length == 0)
			return Array.Empty<GraphemeSpan>();
		int[] starts = StringInfo.ParseCombiningCharacters(text);
		GraphemeSpan[] spans = new GraphemeSpan[starts.Length];
		for (int i = 0; i < starts.Length; i++) {
			int start = starts[i];
			int limit = (i + 1 < starts.Length) ? starts[i + 1] : text.Length;
			spans[i] = new GraphemeSpan(start, limit - start);
		}
		return spans;
	}

	public static LineBreakOpportunity[] GetLineBreaks(string text, string locale) {
		if (text.Length == 0)
			return Array.Empty<LineBreakOpportunity>();
		byte[] brks = GC.AllocateUninitializedArray<byte>(text.Length);
		Unibreak.SetLineBreaks(text, brks, locale);
		List<LineBreakOpportunity> breaks = new();
		for (int i = 0; i < brks.Length; i++) {
			LineBreakKind kind = brks[i] switch {
				0 => LineBreakKind.Hard, // LINEBREAK_MUSTBREAK
				1 => LineBreakKind.Soft, // LINEBREAK_ALLOWBREAK
				_ => LineBreakKind.None,
			};
			if (kind != LineBreakKind.None)
				breaks.Add(new LineBreakOpportunity(i + 1, kind));
		}
		return breaks.ToArray();
	}

	public static LogicalBidiRun[] GetLogicalBidiRuns(string text) {
		if (text.Length == 0)
			return Array.Empty<LogicalBidiRun>();
		using ParagraphAnalysis para = ParagraphAnalysis.Create(text);
		List<LogicalBidiRun> l = new();
		para.GetLogicalRuns(l);
		return l.ToArray();
	}

	public static VisualBidiRun[] GetVisualBidiRunsForLine(string text, int lineStart, int lineLimit) {
		if (lineStart >= lineLimit)
			return Array.Empty<VisualBidiRun>();
		using ParagraphAnalysis para = ParagraphAnalysis.Create(text);
		List<VisualBidiRun> l = new();
		para.GetVisualRunsForLine(l, lineStart, lineLimit);
		return l.ToArray();
	}

	public static TextItem[] ItemizeByScript(ReadOnlySpan<char> text, int sourceStart, Direction direction, string? languageBCP47) {
		static void add(List<TextItem> dst, ReadOnlySpan<char> text, int sourceStart, int runStart, int runLength,
			Direction direction, Script? script, string? languageBCP47) {
			if (runLength <= 0)
				return;
			dst.Add(new TextItem(
				SourceStart: sourceStart + runStart,
				Text: new string(text.Slice(runStart, runLength)),
				Properties: new TextSegmentProperties(direction, script, languageBCP47),
				GuessSegmentProperties: false
			));
		}

		if (text.IsEmpty)
			return Array.Empty<TextItem>();
		List<TextItem> items = new();
		int currentRunStart = -1;
		Script? currentScript = null;
		int pendingPrefixStart = -1;
		int index = 0;
		while (index < text.Length) {
			if (Rune.DecodeFromUtf16(text[index..], out Rune rune, out int runeLength) != OperationStatus.Done)
				throw new InvalidOperationException($"invalid utf-16 at index {index}");
			Script script = hbUnicode.GetScript(rune.Value);
			if (script == Script.Common || script == Script.Inherited || script == Script.Unknown) {
				if (currentRunStart < 0 && pendingPrefixStart < 0)
					pendingPrefixStart = index;
			} else if (currentRunStart < 0) {
				currentRunStart = pendingPrefixStart >= 0 ? pendingPrefixStart : index;
				pendingPrefixStart = -1;
				currentScript = script;
			} else if (currentScript is null || currentScript.Value != script) {
				add(items, text, sourceStart, currentRunStart, index - currentRunStart, direction, currentScript, languageBCP47);
				currentRunStart = index;
				currentScript = script;
			}
			index += runeLength;
		}
		int finalStart = Math.Max(currentRunStart, 0);
		add(items, text, sourceStart, finalStart, text.Length - finalStart,
			direction, currentRunStart >= 0 ? currentScript : Script.Common, languageBCP47);
		return items.ToArray();
	}

	private static bool isRtlLevel(sbyte level) => (level & 1) != 0;
}
