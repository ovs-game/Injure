// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.IO;
using Hexa.NET.SDL2;

using Injure.SDLUtil;

namespace Injure.Core;

public sealed class BootImage {
	private readonly Color32[] data;

	public readonly int Width;
	public readonly int Height;
	public ReadOnlySpan<Color32> Data => data;
	public readonly bool Opaque;

	private BootImage(int width, int height, Color32[] data, bool opaque) {
		Width = width;
		Height = height;
		this.data = data;
		Opaque = opaque;
	}

	public static BootImage LoadFromFile(string path) {
		byte[] file = File.ReadAllBytes(path);
		if (file.Length < 8)
			throw new InvalidDataException("expected at least 8 bytes in file");
		uint wRead = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(0, 4));
		uint hRead = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(4, 4));
		if (wRead == 0 || hRead == 0)
			throw new InvalidDataException("image dimensions must be nonzero");

		int w = checked((int)wRead);
		int h = checked((int)hRead);
		int pxcnt = checked(w * h);
		int expectedSize = checked(8 + pxcnt * 4);
		if (file.Length != expectedSize)
			throw new InvalidDataException($"malformed boot image: expected {expectedSize} bytes, got {file.Length}");

		Color32[] pixels = new Color32[pxcnt];
		bool nonOpaque = false;
		ReadOnlySpan<byte> src = file.AsSpan(8);
		for (int i = 0; i < pxcnt; i++) {
			uint rgba = BinaryPrimitives.ReadUInt32BigEndian(src.Slice(i * 4, 4));
			byte r = (byte)(rgba >> 24);
			byte g = (byte)(rgba >> 16);
			byte b = (byte)(rgba >> 8);
			byte a = (byte)rgba;
			nonOpaque = nonOpaque || a != 0xff;
			pixels[i] = new Color32(r, g, b, a);
		}
		return new BootImage(w, h, pixels, !nonOpaque);
	}
}

// dumb software blitter api for basic drawing before webgpu is up
public sealed unsafe class BootDraw {
	private readonly Action<Color32[], int, int> present;

	public int Width { get; private set; }
	public int Height { get; private set; }
	public int Stride => checked(Width * 4);

	private Color32[] buffer;

	public BootDraw(int width, int height, Action<Color32[], int, int>? present = null) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		Width = width;
		Height = height;
		buffer = new Color32[checked(width * height)];
		this.present = present ?? sdlpresent;
	}

	public void Resize(int width, int height) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		if (Width == width && Height == height)
			return;
		Width = width;
		Height = height;
		buffer = new Color32[checked(width * height)];
	}
	public void Present() => present(buffer, Width, Height);
	public void Clear(Color32 color) => Array.Fill(buffer, color);
	public void Rect(RectI rect, Color32 color) {
		if (!fixrect(ref rect))
			return;
		int start = rect.Y * Width + rect.X;
		for (int y = 0; y < rect.Height; y++) {
			int idx = start + y * Width;
			buffer.AsSpan(idx, rect.Width).Fill(color);
		}
	}
	public void DrawImage(BootImage image, int x, int y) => DrawImage(image, new RectI(0, 0, image.Width, image.Height), x, y);
	public void DrawImage(BootImage image, RectI srcRect, int x, int y) {
		ArgumentNullException.ThrowIfNull(image);
		if (srcRect.Width <= 0 || srcRect.Height <= 0)
			return;
		if (srcRect.X < 0 || srcRect.Y < 0 ||
			srcRect.X + srcRect.Width > image.Width ||
			srcRect.Y + srcRect.Height > image.Height)
			throw new ArgumentOutOfRangeException(nameof(srcRect), "source rect is outside the image");
		int srcX = srcRect.X;
		int srcY = srcRect.Y;
		int width = srcRect.Width;
		int height = srcRect.Height;
		int dstX = x;
		int dstY = y;
		if (dstX < 0) {
			srcX -= dstX;
			width += dstX;
			dstX = 0;
		}
		if (dstY < 0) {
			srcY -= dstY;
			height += dstY;
			dstY = 0;
		}
		if (dstX + width > Width)
			width = Width - dstX;
		if (dstY + height > Height)
			height = Height - dstY;
		if (width <= 0 || height <= 0)
			return;

		fixed (Color32 *src = image.Data)
		fixed (Color32 *dst = buffer) {
			Color32 *srcRow = src + srcY * image.Width + srcX;
			Color32 *dstRow = dst + dstY * Width + dstX;
			if (image.Opaque) {
				nuint rowsz = (nuint)(width * sizeof(Color32));
				for (int row = 0; row < height; row++) {
					Buffer.MemoryCopy(srcRow, dstRow, rowsz, rowsz);
					srcRow += image.Width;
					dstRow += Width;
				}
			} else {
				for (int row = 0; row < height; row++) {
					blend(dstRow, srcRow, width);
					srcRow += image.Width;
					dstRow += Width;
				}
			}
		}
	}

	private bool fixrect(ref RectI rect) {
		if (rect.Width <= 0 || rect.Height <= 0)
			return false;
		int x1 = rect.X;
		int y1 = rect.Y;
		int x2 = checked(rect.X + rect.Width);
		int y2 = checked(rect.Y + rect.Height);
		if (x1 < 0) x1 = 0;
		if (y1 < 0) y1 = 0;
		if (x2 > Width) x2 = Width;
		if (y2 > Height) y2 = Height;
		int w = x2 - x1;
		int h = y2 - y1;
		if (w <= 0 || h <= 0)
			return false;
		rect = new RectI(x1, y1, w, h);
		return true;
	}

	private static void blend(Color32 *dst, Color32 *src, int pixels) {
		for (int i = 0; i < pixels; i++) {
			Color32 s = src[i];
			if (s.A == 0)
				continue;
			if (s.A == 0xff) {
				dst[i] = s;
				continue;
			}
			Color32 d = dst[i];
			int inv = 0xff - s.A;
			byte r = (byte)((s.R * s.A + d.R * inv + 0x7f) / 0xff);
			byte g = (byte)((s.G * s.A + d.G * inv + 0x7f) / 0xff);
			byte b = (byte)((s.B * s.A + d.B * inv + 0x7f) / 0xff);
			byte a = (byte)(s.A + (d.A * inv + 0x7f) / 0xff);
			dst[i] = new Color32(r, g, b, a);
		}
	}

	private static void sdlpresent(Color32[] buffer, int width, int height) {
		SDLSurface *dst = SDL.GetWindowSurface(SDLOwner.Window);
		if (dst is null)
			throw new InvalidOperationException($"SDL_GetWindowSurface: {SDL.GetErrorS()}");
		fixed (Color32 *p = buffer) {
			SDLSurface *src = SDL.CreateRGBSurfaceWithFormatFrom(p, width, height, 32, width * sizeof(Color32), (uint)SDLPixelFormatEnum.Rgba32);
			if (src is null)
				throw new InvalidOperationException($"SDL_CreateRGBSurfaceWithFormatFrom: {SDL.GetErrorS()}");
			uint black = SDL.MapRGBA(dst->Format, 0, 0, 0, 255);
			if (SDL.FillRect(dst, null, black) < 0) {
				SDL.FreeSurface(src);
				throw new InvalidOperationException($"SDL_FillRect: {SDL.GetErrorS()}");
			}
			SDLRect dstRect = fit(width, height, dst->W, dst->H);
			if (SDL.UpperBlitScaled(src, null, dst, &dstRect) < 0) {
				SDL.FreeSurface(src);
				throw new InvalidOperationException($"SDL_UpperBlitScaled: {SDL.GetErrorS()}");
			}
			SDL.FreeSurface(src);
		}
		if (SDL.UpdateWindowSurface(SDLOwner.Window) < 0)
			throw new InvalidOperationException($"SDL_UpdateWindowSurface: {SDL.GetErrorS()}");
	}

	private static SDLRect fit(int srcW, int srcH, int dstW, int dstH) {
		int w, h;
		if ((long)dstW * srcH <= (long)dstH * srcW) {
			w = dstW;
			h = (int)((long)srcH * dstW / srcW);
		} else {
			h = dstH;
			w = (int)((long)srcW * dstH / srcH);
		}
		int x = (dstW - w) / 2;
		int y = (dstH - h) / 2;
		return new SDLRect(x, y, w, h);
	}
}
