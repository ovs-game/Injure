// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Runtime.InteropServices;
using FreeTypeSharp;
using HarfBuzzSharp;
using static FreeTypeSharp.FT;
using static FreeTypeSharp.FT_LOAD;
using static FreeTypeSharp.FT_Render_Mode_;

using Injure.Analyzers.Attributes;
using Injure.Assets;
using System.Diagnostics;

namespace Injure.Graphics.Text;

public readonly record struct FontLineMetrics(
	float Ascent,
	float Descent,
	float LineGap,
	float Height
);

[ClosedEnum]
public readonly partial struct FontRasterMode {
	public enum Case {
		Normal,
		Monochrome
	}
}

[ClosedEnum]
public readonly partial struct FontHinting {
	public enum Case {
		Default,
		None,
		Light,
		Normal
	}
}

public readonly record struct FontOptions(
	int PixelSize,
	FontRasterMode RasterMode = default,
	FontHinting Hinting = default,
	bool UseEmbeddedBitmaps = true
);

internal readonly record struct ResolvedFontKey(
	FontSourceKind SourceKind,
	ulong ID,
	int FaceIndex,
	FontOptions Options
);

internal readonly record struct FontCacheToken(
	ResolvedFontKey Key,
	ulong Version
);

internal interface IResolvedFont : IDisposable {
	ResolvedFontState GetState();
	FontCacheToken GetCacheToken();
}

internal sealed class ResolvedDirectFont(TextSystem owner, Font source, int faceIndex, FontOptions opts) : IResolvedFont {
	private readonly TextSystem owner = owner;
	private readonly Font source = source;
	private readonly int faceIndex = faceIndex;
	private readonly FontOptions opts = opts;

	private ResolvedFontState? state;
	public const ulong Version = 1;

	public ResolvedFontState GetState() {
		if (state is null) {
			LoadedFontFace loadedFace = owner.GetOrCreateLoadedFace(source, faceIndex);
			state = new ResolvedFontState(owner, loadedFace, opts);
		}
		return state;
	}

	public FontCacheToken GetCacheToken() {
		return new FontCacheToken(new ResolvedFontKey(FontSourceKind.Direct, source.ID, faceIndex, opts), Version);
	}

	public void Dispose() => state?.Dispose();
}

internal sealed class ResolvedAssetSourcedFont(TextSystem owner, AssetRef<Font> source, int faceIndex, FontOptions opts) : IResolvedFont {
	private readonly TextSystem owner = owner;
	private readonly AssetRef<Font> source = source;
	private readonly int faceIndex = faceIndex;
	private readonly FontOptions opts = opts;

	private ResolvedFontState? state;
	private ulong loadedVersion;

	private void ensureCurrent() {
		AssetLease<Font> lease = source.Borrow();
		if (state is not null && loadedVersion == lease.Version)
			return;
		LoadedFontFace loadedFace = owner.GetOrCreateLoadedFace(source, lease, faceIndex);
		ResolvedFontState @new = new ResolvedFontState(owner, loadedFace, opts);
		ResolvedFontState? old = state;
		state = @new;
		loadedVersion = lease.Version;
		old?.Dispose();
	}

	public ResolvedFontState GetState() {
		ensureCurrent();
		if (state is null)
			throw new InternalStateException("wasn't expecting state to be null after EnsureCurrent()");
		return state;
	}

	public FontCacheToken GetCacheToken() {
		ensureCurrent();
		return new FontCacheToken(new ResolvedFontKey(FontSourceKind.Asset, source.SlotID, faceIndex, opts), loadedVersion);
	}

	public void Dispose() => state?.Dispose();
}

internal sealed unsafe class LoadedFontFace : IDisposable {
	private readonly byte[] data;
	private readonly GCHandle pinned;
	private bool disposed = false;

	public readonly int FaceIndex;
	public readonly string? DebugName;

	public readonly Blob HbBlob;
	public readonly Face HbFace;

	public int ByteLength => data.Length;
	public byte *DataPtr => (byte *)pinned.AddrOfPinnedObject();

	public LoadedFontFace(Font source, int faceIndex) {
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);

		byte[] data = source.Data.ToArray(); // copy out the data
		GCHandle pinned = default;
		Blob? hbBlob = null;
		Face? hbFace = null;
		try {
			pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
			if (faceIndex >= source.FaceCount)
				throw new ArgumentOutOfRangeException(nameof(faceIndex), $"face index {faceIndex} is out of range for {DebugName ?? "this font"}; file contains {source.FaceCount} face(s)");
			hbBlob = new Blob(pinned.AddrOfPinnedObject(), data.Length, MemoryMode.ReadOnly);
			hbFace = new Face(hbBlob, faceIndex);
			this.data = data;
			this.pinned = pinned;
			FaceIndex = faceIndex;
			DebugName = source.DebugName;
			HbBlob = hbBlob;
			HbFace = hbFace;
			pinned = default;
			hbBlob = null;
			hbFace = null;
		} finally {
			hbFace?.Dispose();
			hbBlob?.Dispose();
			if (pinned.IsAllocated)
				pinned.Free();
		}
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		HbFace.Dispose();
		HbBlob.Dispose();
		if (pinned.IsAllocated)
			pinned.Free();
	}
}

internal readonly record struct FontBackendOptions(
	FT_LOAD LoadFlags,
	FT_Render_Mode_ RenderMode
) {
	public static FontBackendOptions FromOptions(in FontOptions settings) {
		FT_LOAD flags = FT_LOAD_DEFAULT;
		FT_Render_Mode_ renderMode;

		if (!settings.UseEmbeddedBitmaps)
			flags |= FT_LOAD_NO_BITMAP;

		switch (settings.Hinting.Tag) {
		case FontHinting.Case.Default:
			break;
		case FontHinting.Case.None:
			flags |= FT_LOAD_NO_HINTING;
			break;
		case FontHinting.Case.Light:
			flags = clearTargetMode(flags);
			flags |= (FT_LOAD)FT_LOAD_TARGET_LIGHT;
			break;
		case FontHinting.Case.Normal:
			flags = clearTargetMode(flags);
			flags |= FT_LOAD_TARGET_NORMAL;
			break;
		default:
			throw new UnreachableException();
		}

		switch (settings.RasterMode.Tag) {
		case FontRasterMode.Case.Normal:
			renderMode = FT_RENDER_MODE_NORMAL;
			break;
		case FontRasterMode.Case.Monochrome:
			renderMode = FT_RENDER_MODE_MONO;
			flags |= FT_LOAD_MONOCHROME;
			if ((flags & FT_LOAD_NO_HINTING) == 0) {
				flags = clearTargetMode(flags);
				flags |= (FT_LOAD)FT_LOAD_TARGET_MONO;
			}
			break;
		default:
			throw new UnreachableException();
		}

		return new FontBackendOptions(flags, renderMode);
	}

	private static FT_LOAD clearTargetMode(FT_LOAD flags) {
		// FT_LOAD_TARGET_XXX occupies bits 16..19
		const int mask = 0xf << 16;
		return (FT_LOAD)((int)flags & ~mask);
	}
}

internal sealed unsafe class ResolvedFontState : IDisposable {
	private readonly LoadedFontFace loadedFace;
	private readonly FontFunctions hbFontFuncs;
	private bool disposed = false;

	public readonly FT_FaceRec_* FtFace;
	public readonly HarfBuzzSharp.Font HbParentFont;
	public readonly HarfBuzzSharp.Font HbFont;

	internal readonly FontBackendOptions Options;
	public readonly FontLineMetrics LineMetrics;

	public ResolvedFontState(TextSystem owner, LoadedFontFace loadedFace, in FontOptions opts) {
		Options = FontBackendOptions.FromOptions(in opts);
		this.loadedFace = loadedFace;

		FT_FaceRec_ *ftFace = null;
		try {
			FTException.Check(FT_New_Memory_Face(owner.FtLibrary, loadedFace.DataPtr, loadedFace.ByteLength, loadedFace.FaceIndex, &ftFace));
			FTException.Check(FT_Set_Pixel_Sizes(ftFace, 0, (uint)opts.PixelSize));

			HbParentFont = new HarfBuzzSharp.Font(loadedFace.HbFace);
			HbParentFont.SetFunctionsOpenType();
			HbParentFont.SetScale(opts.PixelSize * 64, opts.PixelSize * 64);
			HbFont = new HarfBuzzSharp.Font(HbParentFont);
			HbFont.SetScale(opts.PixelSize * 64, opts.PixelSize * 64);

			hbFontFuncs = new FontFunctions();
			hbFontFuncs.SetNominalGlyphDelegate(hbTryGetNominalGlyph);
			hbFontFuncs.SetHorizontalGlyphAdvanceDelegate(hbGetHorizontalGlyphAdvance);
			hbFontFuncs.SetGlyphExtentsDelegate(hbTryGetGlyphExtents);
			hbFontFuncs.SetHorizontalFontExtentsDelegate(hbTryGetHorizontalFontExtents);
			hbFontFuncs.MakeImmutable();
			HbFont.SetFontFunctions(hbFontFuncs, this);

			FT_Size_Metrics_ m = ftFace->size->metrics;
			float ascent = m.ascender / 64.0f;
			float descent = -m.descender / 64.0f;
			float height = m.height > 0 ? m.height / 64.0f : (ascent + descent);
			float lineGap = MathF.Max(0f, height - (ascent + descent));
			LineMetrics = new FontLineMetrics(
				Ascent: ascent,
				Descent: descent,
				LineGap: lineGap,
				Height: ascent + descent + lineGap
			);
			FtFace = ftFace;
			ftFace = null;
		} finally {
			if (ftFace is not null)
				FT_Done_Face(ftFace);
		}
	}

	public bool HasGlyph(uint codepoint) {
		ObjectDisposedException.ThrowIf(disposed, this);
		return FT_Get_Char_Index(FtFace, codepoint) != 0;
	}

	private bool hbTryGetNominalGlyph(HarfBuzzSharp.Font font, object fontData, uint unicode, out uint glyphID) {
		ObjectDisposedException.ThrowIf(disposed, this);
		return (glyphID = FT_Get_Char_Index(FtFace, unicode)) != 0;
	}

	private int hbGetHorizontalGlyphAdvance(HarfBuzzSharp.Font font, object fontData, uint glyphID) {
		ObjectDisposedException.ThrowIf(disposed, this);
		nint advance16_16 = 0;
		FTException.Check(FT_Get_Advance(FtFace, glyphID, Options.LoadFlags, &advance16_16));
		return (int)((advance16_16 + 0x200) >> 10);
	}

	private bool hbTryGetGlyphExtents(HarfBuzzSharp.Font font, object fontData, uint glyphID, out GlyphExtents extents) {
		ObjectDisposedException.ThrowIf(disposed, this);
		FTException.Check(FT_Load_Glyph(FtFace, glyphID, Options.LoadFlags));
		FT_Glyph_Metrics_ m = FtFace->glyph->metrics;
		extents = new GlyphExtents {
			XBearing = checked((int)m.horiBearingX),
			YBearing = checked((int)m.horiBearingY),
			Width = checked((int)m.width),
			Height = checked((int)-m.height)
		};
		return true;
	}

	private bool hbTryGetHorizontalFontExtents(HarfBuzzSharp.Font font, object fontData, out FontExtents extents) {
		ObjectDisposedException.ThrowIf(disposed, this);
		FT_Size_Metrics_ m = FtFace->size->metrics;
		extents = new FontExtents {
			Ascender = checked((int)m.ascender),
			Descender = checked((int)m.descender),
			LineGap = checked((int)(m.height - (m.ascender - m.descender)))
		};
		return true;
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		hbFontFuncs.Dispose();
		HbFont.Dispose();
		HbParentFont.Dispose();
		FT_Done_Face(FtFace);
	}
}
