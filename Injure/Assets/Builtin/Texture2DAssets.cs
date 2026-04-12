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
			TextureFormat fmt = p.Metadata.SRGB ? TextureFormat.RGBA8UnormSrgb : TextureFormat.RGBA8Unorm;
			GPUSamplerCreateParams smpParams = p.Metadata.SamplerMode switch {
				Texture2DSamplerMode.NearestClamp => SamplerStates.NearestClamp,
				Texture2DSamplerMode.LinearClamp => SamplerStates.LinearClamp,
				Texture2DSamplerMode.NearestRepeat => SamplerStates.NearestRepeat,
				Texture2DSamplerMode.LinearRepeat => SamplerStates.LinearRepeat,
				_ => throw new UnreachableException()
			};
			GPUTextureRegion texRegion;
			GPUTextureLayout texLayout;
			if (p.Metadata.SourceRect is RectI r) {
				if (r.Width <= 0 || r.Height <= 0) throw new ArgumentException("texture source rect cannot have negative/zero dimensions");
				if (r.X < 0) throw new ArgumentException("texture source rect goes out of bounds (negative X)");
				if (r.Y < 0) throw new ArgumentException("texture source rect goes out of bounds (negative Y)");
				if (r.X + r.Width > p.Width) throw new ArgumentException("texture source rect goes out of bounds (X + width > texture width)");
				if (r.Y + r.Height > p.Height) throw new ArgumentException("texture source rect goes out of bounds (Y + height > texture height)");
				texRegion = new GPUTextureRegion(X: 0, Y: 0, Z: 0, Width: (uint)r.Width, Height: (uint)r.Height);
				texLayout = new GPUTextureLayout(Offset: (ulong)(r.Y * (p.Width * 4) + r.X * 4), BytesPerRow: p.Width * 4, RowsPerImage: p.Height);
			} else {
				texRegion = new GPUTextureRegion(X: 0, Y: 0, Z: 0, Width: p.Width, Height: p.Height);
				texLayout = new GPUTextureLayout(Offset: 0, BytesPerRow: p.Width * 4, RowsPerImage: p.Height);
			}

			GPUTexture tex = gpuDevice.CreateTexture(new GPUTextureCreateParams(
				Width: p.Width,
				Height: p.Height,
				DepthOrArrayLayers: 1,
				MipLevelCount: 1,
				SampleCount: 1,
				Dimension: TextureDimension.Dimension2D,
				Format: fmt,
				Usage: TextureUsage.TextureBinding | TextureUsage.CopyDst
			));
			// TODO: don't do This thing with the try { try {} catch { throw; } } catch { throw; } it's ugly
			try {
				GPUSampler sampler = gpuDevice.CreateSampler(in smpParams);
				try {
					gpuDevice.WriteToTexture(tex, in texRegion, p.RGBA, in texLayout);
					return AssetCreateResult<Texture2D>.Success(new Texture2D(gpuDevice, tex, sampler));
				} catch {
					sampler.Dispose();
					throw;
				}
			} catch {
				tex.Dispose();
				throw;
			}
		} catch (Exception ex) {
			return AssetCreateResult<Texture2D>.Error(
				new AssetLoadException(info.AssetID, typeof(Texture2D), "failed to finalize texture", ex));
		}
	}
}
