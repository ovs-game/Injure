// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using StbImageSharp;

using Injure.Graphics;
using Injure.Graphics.PixelConv;
using Injure.Rendering;

namespace Injure.Assets.Builtin;

public sealed class Texture2DSamplerModeJsonConverter : JsonStringEnumConverter<Texture2DSamplerMode> {
	public Texture2DSamplerModeJsonConverter() : base(namingPolicy: null, allowIntegerValues: false) {}
}

[JsonConverter(typeof(Texture2DSamplerModeJsonConverter))]
public enum Texture2DSamplerMode {
	NearestClamp,
	LinearClamp,
	NearestRepeat,
	LinearRepeat
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class Texture2DAssetMetadata {
	public required AssetID Source { get; init; }
	public RectI? SourceRect { get; init; } = null;
	public bool SRGB { get; init; } = true;
	public Texture2DSamplerMode SamplerMode { get; init; } = Texture2DSamplerMode.NearestClamp;
}

public sealed class Texture2DAssetData(Stream stream, Texture2DAssetMetadata metadata,
	string debugName, string? suggestedExtension = null, object? origin = null) : AssetData(debugName, suggestedExtension, origin) {
	public readonly Stream Stream = stream;
	public readonly Texture2DAssetMetadata Metadata = metadata;
}

public sealed class Texture2DJsonAssetResolver : IAssetResolverAsync {
	public async Task<AssetResolveResult> TryResolveAsync(AssetResolveAsyncInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		try {
			if (!info.AssetID.Path.EndsWith(".tex.json", StringComparison.Ordinal))
				return AssetResolveResult.NotHandled();
			await using Stream jsonStream = await info.FetchAsync(info.AssetID, ct).ConfigureAwait(false);
			Texture2DAssetMetadata meta;
			if (!mightBeJson(jsonStream))
				return AssetResolveResult.NotHandled();
			try {
				meta = await JsonSerializer.DeserializeAsync(jsonStream, InjureJsonContext.Default.Texture2DAssetMetadata, cancellationToken: ct).ConfigureAwait(false) ??
					throw new JsonException("expected nonnull json");
			} catch (JsonException) {
				return AssetResolveResult.NotHandled();
			}
			ct.ThrowIfCancellationRequested();
			Stream imgStream = await info.FetchAsync(meta.Source, ct).ConfigureAwait(false);
			return AssetResolveResult.Success(new Texture2DAssetData(imgStream, meta,
				meta.Source.ToString(), Path.GetExtension(meta.Source.Path), meta.Source));
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception ex) {
			return AssetResolveResult.Error(ex);
		}
	}

	private static bool mightBeJson(Stream stream) {
		long saved = stream.Position;
		try {
			int b;
			do {
				b = stream.ReadByte();
				if (b < 0)
					return false;
			} while (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n');
			return b is (byte)'{' or (byte)'[' or (byte)'"' or (byte)'t' or (byte)'f' or (byte)'n' or (byte)'-' ||
				(b >= (byte)'0' && b <= (byte)'9');
		} finally {
			stream.Position = saved;
		}
	}
}

public sealed class Texture2DImageAssetResolver : IAssetResolverAsync {
	public async Task<AssetResolveResult> TryResolveAsync(AssetResolveAsyncInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		try {
			Stream imgStream = await info.FetchAsync(info.AssetID, ct).ConfigureAwait(false);
			Texture2DAssetMetadata meta = new Texture2DAssetMetadata { Source = info.AssetID };
			ImageInfo? imageInfo = ImageInfo.FromStream(imgStream);
			if (imageInfo is null) {
				await imgStream.DisposeAsync();
				return AssetResolveResult.NotHandled();
			}
			imgStream.Position = 0;
			return AssetResolveResult.Success(new Texture2DAssetData(imgStream, meta,
				info.AssetID.ToString(), Path.GetExtension(info.AssetID.Path), info.AssetID));
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception ex) {
			return AssetResolveResult.Error(ex);
		}
	}
}

public sealed class Texture2DAssetPreparedData(uint width, uint height, byte[] rgba, Texture2DAssetMetadata metadata) : AssetPreparedData {
	public readonly uint Width = width;
	public readonly uint Height = height;
	public readonly byte[] RGBA = rgba;
	public readonly Texture2DAssetMetadata Metadata = metadata;
}

public sealed class Texture2DAssetCreator(WebGPUDevice gpuDevice) : IAssetCreatorAsync<Texture2D> {
	private readonly WebGPUDevice gpuDevice = gpuDevice;

	public async Task<AssetCreatePreparedResult> TryCreateAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (info.Data is not Texture2DAssetData data)
			return AssetCreatePreparedResult.NotHandled();
		try {
			await using Stream stream = data.Stream;
			ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
			if (image.Width <= 0 || image.Height <= 0)
				return AssetCreatePreparedResult.Error(new AssetLoadException(info.AssetID, typeof(Texture2D), "image decode returned bogus dimensions"));
			ct.ThrowIfCancellationRequested();
			return AssetCreatePreparedResult.Success(new Texture2DAssetPreparedData((uint)image.Width, (uint)image.Height, image.Data, data.Metadata));
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception ex) {
			return AssetCreatePreparedResult.Error(new AssetLoadException(info.AssetID, typeof(Texture2D), "failed to decode image", ex));
		}
	}

	public AssetCreateResult<Texture2D> TryFinalize(AssetFinalizeInfo info) {
		if (info.Prepared is not Texture2DAssetPreparedData p)
			return AssetCreateResult<Texture2D>.NotHandled();
		try {
			Texture2DFormat fmt = p.Metadata.SRGB ? Texture2DFormat.RGBA32_UNorm_Srgb : Texture2DFormat.RGBA32_UNorm;
			GPUSamplerCreateParams smpParams = p.Metadata.SamplerMode switch {
				Texture2DSamplerMode.NearestClamp => SamplerStates.NearestClamp,
				Texture2DSamplerMode.LinearClamp => SamplerStates.LinearClamp,
				Texture2DSamplerMode.NearestRepeat => SamplerStates.NearestRepeat,
				Texture2DSamplerMode.LinearRepeat => SamplerStates.LinearRepeat,
				_ => throw new UnreachableException()
			};
			ReadOnlySpan<byte> src;
			uint srcStride, w, h;
			if (p.Metadata.SourceRect is RectI r) {
				if (r.Width <= 0 || r.Height <= 0) throw new ArgumentException("texture source rect cannot have negative/zero dimensions");
				if (r.X < 0) throw new ArgumentException("texture source rect goes out of bounds (negative X)");
				if (r.Y < 0) throw new ArgumentException("texture source rect goes out of bounds (negative Y)");
				if (r.X + r.Width > p.Width) throw new ArgumentException("texture source rect goes out of bounds (X + width > texture width)");
				if (r.Y + r.Height > p.Height) throw new ArgumentException("texture source rect goes out of bounds (Y + height > texture height)");

				w = (uint)r.Width;
				h = (uint)r.Height;
				srcStride = p.Width * 4;
				src = p.RGBA.AsSpan(checked((int)(r.Y * (p.Width * 4) + r.X * 4)));
			} else {
				w = p.Width;
				h = p.Height;
				srcStride = p.Width * 4;
				src = p.RGBA;
			}

			Texture2D tex = new Texture2D(gpuDevice, w, h, fmt);
			tex.Upload(src, checked((int)srcStride), PixelFormat.RGBA32_UNorm);
			return AssetCreateResult<Texture2D>.Success(tex);
		} catch (Exception ex) {
			return AssetCreateResult<Texture2D>.Error(
				new AssetLoadException(info.AssetID, typeof(Texture2D), "failed to finalize texture", ex));
		}
	}
}
