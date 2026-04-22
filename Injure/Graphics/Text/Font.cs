// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Threading;

using Injure.Assets;

namespace Injure.Graphics.Text;

public sealed class Font : IRevokable {
	private static ulong nextID = 0;
	private int revoked = 0;

	internal ulong ID { get; }
	internal byte[] Data { get { chk(); return field; } }
	public string? DebugName { get { chk(); return field; } }
	public int FaceCount { get { chk(); return field; } }

	public Font(byte[] data, string? debugName, int faceCount) {
		ArgumentNullException.ThrowIfNull(data);
		ID = Interlocked.Increment(ref nextID);
		Data = data;
		DebugName = debugName;
		FaceCount = faceCount;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void chk() {
		if (Volatile.Read(ref revoked) != 0)
			throw new AssetLeaseExpiredException("borrowed font was used after its lease expired");
	}

	public void Revoke() => Volatile.Write(ref revoked, 1);
}

internal enum FontSourceKind {
	Direct,
	Asset
}

public readonly struct FontSpec {
	private readonly Font? direct;
	private readonly AssetRef<Font>? asset;

	public int FaceIndex { get; }

	internal FontSourceKind SourceKind { get; }
	internal Font Direct => (SourceKind == FontSourceKind.Direct) ? direct! : throw new InvalidOperationException("this FontSpec is not sourced from a Font");
	internal AssetRef<Font> Asset => (SourceKind == FontSourceKind.Asset) ? asset! : throw new InvalidOperationException("this FontSpec is not sourced from an AssetRef<Font>");

	public FontSpec(Font font, int faceIndex = 0) {
		ArgumentNullException.ThrowIfNull(font);
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
		direct = font;
		SourceKind = FontSourceKind.Direct;
		FaceIndex = faceIndex;
	}

	public FontSpec(AssetRef<Font> font, int faceIndex = 0) {
		ArgumentNullException.ThrowIfNull(font);
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
		asset = font;
		SourceKind = FontSourceKind.Asset;
		FaceIndex = faceIndex;
	}

	public static implicit operator FontSpec(Font font) => new FontSpec(font);
	public static implicit operator FontSpec(AssetRef<Font> font) => new FontSpec(font);
}

public static class FontSpecExtensions {
	public static FontSpec Face(this Font font, int faceIndex) => new FontSpec(font, faceIndex);
	public static FontSpec Face(this AssetRef<Font> font, int faceIndex) => new FontSpec(font, faceIndex);
}
