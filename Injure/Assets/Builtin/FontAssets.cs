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

public sealed class FontAssetResolver : IAssetResolverAsync {
	public async Task<AssetResolveResult> TryResolveAsync(AssetResolveAsyncInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		try {
			Stream stream = await info.FetchAsync(info.AssetID, ct).ConfigureAwait(false);
			if (!looksLikeAFont(stream)) {
				await stream.DisposeAsync().ConfigureAwait(false);
				return AssetResolveResult.NotHandled();
			}
			return AssetResolveResult.Success(new FontAssetData(stream,
				info.AssetID.ToString(), Path.GetExtension(info.AssetID.Path), info.AssetID));
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception ex) {
			return AssetResolveResult.Error(ex);
		}
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

public sealed class FontPreparedData(byte[] bytes, string debugName) : AssetPreparedData {
	public readonly byte[] Bytes = bytes;
	public readonly string DebugName = debugName;
}

public sealed class FontAssetCreator(TextSystem text) : IAssetCreatorAsync<Font> {
	private readonly TextSystem text = text;

	public async Task<AssetCreatePreparedResult> TryCreateAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (info.Data is not FontAssetData data)
			return AssetCreatePreparedResult.NotHandled();
		try {
			await using Stream stream = data.Stream;
			byte[] bytes = await readAllBytesAsync(stream, ct).ConfigureAwait(false);
			if (bytes.Length < 4)
				return AssetCreatePreparedResult.Error(
					new AssetLoadException(info.AssetID, typeof(Font), "font file is too small"));
			return AssetCreatePreparedResult.Success(new FontPreparedData(bytes, data.DebugName));
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception ex) {
			return AssetCreatePreparedResult.Error(
				new AssetLoadException(info.AssetID, typeof(Font), "failed to read font data", ex));
		}
	}

	public AssetCreateResult<Font> TryFinalize(AssetFinalizeInfo info) {
		if (info.Prepared is not FontPreparedData p)
			return AssetCreateResult<Font>.NotHandled();
		try {
			return AssetCreateResult<Font>.Success(text.LoadFont(p.Bytes, p.DebugName));
		} catch (Exception ex) {
			return AssetCreateResult<Font>.Error(
				new AssetLoadException(info.AssetID, typeof(Font), "unsupported or invalid font", ex));
		}
	}

	private static async Task<byte[]> readAllBytesAsync(Stream stream, CancellationToken ct) {
		using MemoryStream ms = new MemoryStream();
		await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
		return ms.ToArray();
	}
}
