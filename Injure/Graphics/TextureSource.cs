// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

using Injure.Assets;
using Injure.Rendering;

namespace Injure.Graphics;

public enum TextureSourceKind {
	Texture2D,
	RenderTarget2D,
	Texture2DAssetRef
}

public readonly struct TextureSource : IEquatable<TextureSource> {
	private readonly object val;
	public TextureSourceKind Kind { get; }

	private TextureSource(object val, TextureSourceKind kind) {
		ArgumentNullException.ThrowIfNull(val);
		this.val = val;
		Kind = kind;
	}

	public static implicit operator TextureSource(Texture2D tex) =>
		new TextureSource(tex, TextureSourceKind.Texture2D);
	public static implicit operator TextureSource(RenderTarget2D rt) =>
		new TextureSource(rt, TextureSourceKind.RenderTarget2D);
	public static implicit operator TextureSource(AssetRef<Texture2D> asset) =>
		new TextureSource(asset, TextureSourceKind.Texture2DAssetRef);

	public bool Equals(TextureSource other) => ReferenceEquals(val, other.val) && Kind == other.Kind;
	public override bool Equals(object? obj) => obj is TextureSource other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(val), (int)Kind);
	public static bool operator ==(TextureSource left, TextureSource right) => left.Equals(right);
	public static bool operator !=(TextureSource left, TextureSource right) => !left.Equals(right);

	internal ResolvedTextureSource Resolve() => Kind switch {
		TextureSourceKind.Texture2D => new ResolvedTextureSource((Texture2D)val),
		TextureSourceKind.RenderTarget2D => new ResolvedTextureSource((RenderTarget2D)val),
		TextureSourceKind.Texture2DAssetRef => new ResolvedTextureSource(((AssetRef<Texture2D>)val).Borrow()),
		_ => throw new UnreachableException()
	};
}

internal enum ResolvedTextureSourceKind {
	Texture2D,
	RenderTarget2D,
	LeasedTexture2D
}

internal readonly ref struct ResolvedTextureSource {
	private readonly Texture2D? texture = null;
	private readonly RenderTarget2D? renderTarget = null;
	private readonly AssetLease<Texture2D> lease = default;

	public ResolvedTextureSourceKind Kind { get; }

	public ResolvedTextureSource(Texture2D texture) {
		this.texture = texture;
		Kind = ResolvedTextureSourceKind.Texture2D;
	}

	public ResolvedTextureSource(RenderTarget2D renderTarget) {
		this.renderTarget = renderTarget;
		Kind = ResolvedTextureSourceKind.RenderTarget2D;
	}

	public ResolvedTextureSource(AssetLease<Texture2D> lease) {
		this.lease = lease;
		Kind = ResolvedTextureSourceKind.LeasedTexture2D;
	}

	public uint Width => Kind switch {
		ResolvedTextureSourceKind.Texture2D => texture!.Width,
		ResolvedTextureSourceKind.RenderTarget2D => renderTarget!.Width,
		ResolvedTextureSourceKind.LeasedTexture2D => lease.Value.Width,
		_ => throw new UnreachableException()
	};
	public uint Height => Kind switch {
		ResolvedTextureSourceKind.Texture2D => texture!.Height,
		ResolvedTextureSourceKind.RenderTarget2D => renderTarget!.Height,
		ResolvedTextureSourceKind.LeasedTexture2D => lease.Value.Height,
		_ => throw new UnreachableException()
	};
	public TextureFormat Format => Kind switch {
		ResolvedTextureSourceKind.Texture2D => texture!.Format,
		ResolvedTextureSourceKind.RenderTarget2D => renderTarget!.ColorFormat,
		ResolvedTextureSourceKind.LeasedTexture2D => lease.Value.Format,
		_ => throw new UnreachableException()
	};
	public GPUBindGroupRef BindGroup => Kind switch {
		ResolvedTextureSourceKind.Texture2D => texture!.BindGroup,
		ResolvedTextureSourceKind.RenderTarget2D => renderTarget!.ColorBindGroup,
		ResolvedTextureSourceKind.LeasedTexture2D => lease.Value.BindGroup,
		_ => throw new UnreachableException()
	};

	public object Identity => Kind switch {
		ResolvedTextureSourceKind.Texture2D => texture!,
		ResolvedTextureSourceKind.RenderTarget2D => renderTarget!,
		ResolvedTextureSourceKind.LeasedTexture2D => lease.Value,
		_ => throw new UnreachableException()
	};

	// comparing color textures only is enough since every render target has its own one
	public bool SameRenderTargetAs(RenderTarget2D rt) =>
		Kind == ResolvedTextureSourceKind.RenderTarget2D && renderTarget!.ColorTexture.SameTexture(rt.ColorTexture);
}
