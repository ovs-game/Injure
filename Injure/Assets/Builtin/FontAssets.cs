// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Injure.Graphics.Text;

namespace Injure.Assets.Builtin;

public sealed class FontAssetData(Stream stream,
	string debugName, string? suggestedExtension = null, object? origin = null) : AssetData(debugName, suggestedExtension, origin) {
	public readonly Stream Stream = stream;
}

public sealed class FontAssetResolver : IAssetResolver {
	public async ValueTask<AssetResolveResult> TryResolveAsync(AssetResolveInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		Stream stream = await info.FetchAsync(info.AssetID, ct).ConfigureAwait(false);
		if (!looksLikeAFont(stream)) {
			await stream.DisposeAsync().ConfigureAwait(false);
			return AssetResolveResult.NotHandled();
		}
		return AssetResolveResult.Success(new FontAssetData(stream,
			info.AssetID.ToString(), Path.GetExtension(info.AssetID.Path), info.AssetID));
	}

	private static bool looksLikeAFont(Stream stream) {
		long saved = stream.Position;
		try {
			Span<byte> hdr = stackalloc byte[4];
			if (stream.Read(hdr) != 4)
				return false;
			return (((uint)hdr[0] << 24) | ((uint)hdr[1] << 16) | ((uint)hdr[2] << 8) | hdr[3]) is
				0x00010000 or // truetype / opentype with truetype outlines
				0x4f54544f or // 'OTTO' (opentype with cff/cff2 outlines)
				0x74746366 or // 'ttcf' (truetype collection)
				0x774f4646 or // 'wOFF' (woff 1)
				0x774f4632 or // 'wOF2' (woff 2)
				0x74727565;   // 'true' (old apple tag for sfnt-wrapped truetype)
		} finally {
			stream.Position = saved;
		}
	}
}

public sealed class FontAssetCreator(TextSystem text) : IAssetCreator<Font> {
	private readonly TextSystem text = text;

	public async ValueTask<AssetCreateResult<Font>> TryCreateAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (info.Data is not FontAssetData data)
			return AssetCreateResult<Font>.NotHandled();
		await using Stream stream = data.Stream;
		byte[] bytes = await readAllBytesAsync(stream, ct).ConfigureAwait(false);
		if (bytes.Length < 4)
			throw new AssetLoadException(info.AssetID, typeof(Font), "font file is too small");
		return AssetCreateResult<Font>.Success(text.LoadFont(bytes, data.DebugName)); // XXX: pretty sure LoadFont isn't thread safe yet
	}

	private static async Task<byte[]> readAllBytesAsync(Stream stream, CancellationToken ct) {
		using MemoryStream ms = new MemoryStream();
		await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
		return ms.ToArray();
	}
}
