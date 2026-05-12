// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FreeTypeSharp;
using static FreeTypeSharp.FT;

using Injure.Rendering;

namespace Injure.Graphics.Text;

internal sealed class GlyphAtlasPage : IDisposable {
	public required Texture2D Texture;
	public required int Width;
	public required int Height;

	public int WriteX;
	public int WriteY;
	public int RowHeight;

	public ulong LastUseStamp;
	public readonly HashSet<GlyphAtlasKey> Keys = new();

	private int refcount;
	public int RefCount => Volatile.Read(ref refcount);

	public void Retain() {
		if (Interlocked.Increment(ref refcount) <= 0)
			throw new InternalStateException("refcount overflow/corruption");
	}

	public void Release() {
		if (Interlocked.Decrement(ref refcount) < 0)
			throw new InternalStateException("refcount went negative");
	}

	public void Dispose() => Texture.Dispose();
}

internal readonly record struct GlyphAtlasKey(
	FontCacheToken FontCacheToken,
	uint GlyphID
);

internal readonly record struct GlyphAtlasEntry(
	GlyphAtlasPage Page,
	RectI SrcPixels,
	int BitmapLeft,
	int BitmapTop,
	int Width,
	int Height
);

internal sealed unsafe class GlyphAtlas(WebGPUDevice gpuDevice, TextSystem text, int pageWidth = 1024, int pageHeight = 1024, int padding = 1, int maxPages = 16) : IDisposable {
	private readonly WebGPUDevice gpuDevice = gpuDevice;
	private readonly TextSystem text = text;
	private readonly Dictionary<GlyphAtlasKey, GlyphAtlasEntry> entries = new();
	private readonly List<GlyphAtlasPage> pages = new();

	private readonly int pageWidth = pageWidth;
	private readonly int pageHeight = pageHeight;
	private readonly int padding = padding;
	private readonly int maxPages = maxPages;

	private ulong nextUseStamp = 0; // first will be 1 since this gets incremented upfront
	private bool disposed = false;

	private void clear() {
		for (int i = 0; i < pages.Count; i++)
			pages[i].Dispose();
		pages.Clear();
		entries.Clear();
	}

	public void Clear() {
		ObjectDisposedException.ThrowIf(disposed, this);
		clear();
	}

	public bool TryGetOrCreate(IResolvedFont font, uint glyphID, out GlyphAtlasEntry entry) {
		ObjectDisposedException.ThrowIf(disposed, this);
		GlyphAtlasKey key = new(
			FontCacheToken: font.GetCacheToken(),
			GlyphID: glyphID
		);
		if (entries.TryGetValue(key, out entry)) {
			entry.Page.LastUseStamp = ++nextUseStamp;
			return true;
		}
		if (tryRasterize(font, glyphID, out entry)) {
			entry.Page.Keys.Add(key);
			entry.Page.LastUseStamp = +nextUseStamp;
			entries.Add(key, entry);
			text.OnCacheActivity();
			Trim();
			return true;
		}
		return false;
	}

	private bool tryRasterize(IResolvedFont font, uint glyphID, out GlyphAtlasEntry entry) {
		ResolvedFontState st = font.GetState();
		FTException.Check(FT_Load_Glyph(st.FtFace, glyphID, st.Options.LoadFlags));
		FTException.Check(FT_Render_Glyph(st.FtFace->glyph, st.Options.RenderMode));
		FT_GlyphSlotRec_ *slot = st.FtFace->glyph;
		int bitmapLeft = slot->bitmap_left;
		int bitmapTop = slot->bitmap_top;
		int w = checked((int)slot->bitmap.width);
		int h = checked((int)slot->bitmap.rows);
		if (slot->bitmap.width == 0 || slot->bitmap.rows == 0) {
			entry = default;
			return false;
		}

		byte[] pixels = readbitmap(slot->bitmap);
		GlyphAtlasPage page = alloc(w, h, out int x, out int y);
		page.Texture.Upload(x, y, pixels, srcStride: w, PixelConv.PixelFormat.R8_UNorm, w, h);
		entry = new GlyphAtlasEntry(
			Page: page,
			SrcPixels: new RectI(x, y, w, h),
			BitmapLeft: bitmapLeft,
			BitmapTop: bitmapTop,
			Width: w,
			Height: h
		);
		return true;
	}

	private static byte[] readbitmap(FT_Bitmap_ bitmap) {
		int w = (int)bitmap.width;
		int h = (int)bitmap.rows;
		int pitch = bitmap.pitch;
		if (bitmap.pixel_mode != FT_Pixel_Mode_.FT_PIXEL_MODE_GRAY)
			throw new NotSupportedException($"unsupported FreeType pixel mode {bitmap.pixel_mode}");
		byte[] buf = new byte[checked(w * h)];
		if (bitmap.buffer is null || w == 0 || h == 0)
			return buf;
		fixed (byte *dst = buf) {
			if (pitch == w) {
				ulong sz = (ulong)(w * h);
				Buffer.MemoryCopy(bitmap.buffer, dst, sz, sz);
			} else {
				// negative pitch = rows are bottom-up
				int absPitch = Math.Abs(pitch);
				for (int y = 0; y < h; y++) {
					byte *srcRow = (pitch >= 0) ? (bitmap.buffer + y * absPitch) : (bitmap.buffer + (h - 1 - y) * absPitch);
					byte *dstRow = dst + y * w;
					Buffer.MemoryCopy(srcRow, dstRow, (ulong)w, (ulong)w);
				}
			}
		}
		return buf;
	}

	private GlyphAtlasPage alloc(int width, int height, out int x, out int y) {
		int w = width + padding * 2;
		int h = height + padding * 2;
		int px, py;
		for (int i = 0; i < pages.Count; i++) {
			if (tryAllocOnPage(pages[i], w, h, out px, out py)) {
				x = px + padding;
				y = py + padding;
				return pages[i];
			}
		}
		GlyphAtlasPage page = newpage();
		if (!tryAllocOnPage(page, w, h, out px, out py))
			throw new InvalidOperationException("glyph is larger than atlas page");
		x = px + padding;
		y = py + padding;
		return page;
	}

	private GlyphAtlasPage newpage() {
		GlyphAtlasPage page = new() {
			Texture = new Texture2D(gpuDevice, new Texture2DCreateParams(
				Width: (uint)pageWidth,
				Height: (uint)pageHeight,
				Format: Texture2DFormat.R8_UNorm,
				SamplerParams: SamplerStates.LinearClamp
			)),
			Width = pageWidth,
			Height = pageHeight,
			WriteX = 0,
			WriteY = 0,
			RowHeight = 0,
		};
		pages.Add(page);
		return page;
	}

	private static bool tryAllocOnPage(GlyphAtlasPage page, int width, int height, out int x, out int y) {
		if (page.WriteX + width > page.Width) {
			page.WriteX = 0;
			page.WriteY += page.RowHeight;
			page.RowHeight = 0;
		}
		if (page.WriteY + height > page.Height) {
			x = 0;
			y = 0;
			return false;
		}
		x = page.WriteX;
		y = page.WriteY;
		page.WriteX += width;
		page.RowHeight = Math.Max(page.RowHeight, height);
		return true;
	}

	public void Trim() {
		if (pages.Count <= maxPages)
			return;
		foreach (GlyphAtlasPage page in pages
			.Where(static p => p.RefCount == 0)
			.OrderBy(static p => p.LastUseStamp)
			.Take(pages.Count - maxPages)) {
			foreach (GlyphAtlasKey key in page.Keys)
				entries.Remove(key);
			page.Keys.Clear();
			pages.Remove(page);
			page.Dispose();
		}
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		clear();
	}
}
