// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Silk.NET.WebGPU;

using Injure.Assets;
using Injure.Rendering;

namespace Injure.Graphics;

/// <summary>
/// Identifies the current draw target for a <see cref="Canvas"/>.
/// </summary>
/// <remarks>
/// A target is either the backbuffer sentinel <see cref="Backbuffer"/> or an
/// offscreen <see cref="RenderTarget2D"/>. <see cref="RenderTarget2D"/> has an
/// implicit cast to <see cref="CanvasTarget"/> for convenience.
/// </remarks>
public readonly struct CanvasTarget : IEquatable<CanvasTarget> {
	/// <summary>
	/// Sentinel value representing the backbuffer.
	/// </summary>
	public static readonly CanvasTarget Backbuffer = new CanvasTarget(true, null);

	/// <summary>
	/// Whether this target represents the backbuffer.
	/// </summary>
	[MemberNotNullWhen(false, nameof(rtBacking), nameof(RenderTarget))]
	public bool IsBackbuffer { get; }

	/// <summary>
	/// Gets the <see cref="RenderTarget2D"/> wrapped by this canvas target.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this target represents the backbuffer.
	/// </exception>
	public readonly RenderTarget2D RenderTarget {
		get {
			if (IsBackbuffer)
				throw new InvalidOperationException("backbuffer has no RenderTarget2D");
			return rtBacking;
		}
	}
	private readonly RenderTarget2D? rtBacking;

	/// <summary>
	/// Creates a canvas target from an offscreen render target.
	/// </summary>
	/// <param name="renderTarget">Target to draw into.</param>
	public CanvasTarget(RenderTarget2D renderTarget) {
		IsBackbuffer = false;
		rtBacking = renderTarget;
	}

	private CanvasTarget(bool isBackbuffer, RenderTarget2D? renderTarget) {
		IsBackbuffer = isBackbuffer;
		if (isBackbuffer ^ renderTarget is null)
			throw new InternalStateException("tried to create invalid CanvasTarget state");
		rtBacking = renderTarget;
	}

	public bool Equals(CanvasTarget other) => IsBackbuffer == other.IsBackbuffer && ReferenceEquals(rtBacking, other.rtBacking);
	public override bool Equals(object? obj) => obj is CanvasTarget other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(IsBackbuffer, rtBacking);
	public static bool operator ==(CanvasTarget left, CanvasTarget right) => left.Equals(right);
	public static bool operator !=(CanvasTarget left, CanvasTarget right) => !left.Equals(right);

	/// <summary>
	/// Implicitly converts a <see cref="RenderTarget2D"/> to a <see cref="CanvasTarget"/>.
	/// </summary>
	public static implicit operator CanvasTarget(RenderTarget2D target) => new CanvasTarget(target);
}

/// <summary>
/// Describes how a <see cref="CanvasScissor"/> should be interpreted.
/// </summary>
public enum CanvasScissorKind {
	/// <summary>
	/// Disable clipping (use the full active target as the scissor).
	/// </summary>
	None,

	/// <summary>
	/// Set the current scissor to the given rectangle.
	/// </summary>
	Set,

	/// <summary>
	/// Intersect the current scissor with the given rectangle. Only valid on
	/// overrides, not base params.
	/// </summary>
	Intersect
}

/// <summary>
/// Describes a scissor rect for use in <see cref="Canvas"/>.
/// </summary>
/// <remarks>
/// Scissor rects are not affected by <see cref="CanvasParams.Transform"/>, as
/// they are pass state, not geometry.
/// </remarks>
public readonly struct CanvasScissor : IEquatable<CanvasScissor> {
	/// <summary>
	/// How this <see cref="CanvasScissor"/>'s value should be interpreted.
	/// </summary>
	public readonly CanvasScissorKind Kind;

	/// <summary>
	/// Scissor rectangle.
	/// </summary>
	/// <remarks>
	/// Ignored for <see cref="CanvasScissorKind.None"/>.
	/// </remarks>
	public readonly RectI Rect;

	private CanvasScissor(CanvasScissorKind kind, RectI rect) {
		Kind = kind;
		Rect = rect;
	}

	/// <summary>
	/// Disable clipping (use the full active target as the scissor).
	/// </summary>
	public static readonly CanvasScissor None = new CanvasScissor(CanvasScissorKind.None, default);

	/// <summary>
	/// Creates a scissor to set the current one to <paramref name="rect"/>.
	/// </summary>
	/// <param name="rect">Scissor rect, in pixel coordinates.</param>
	public static CanvasScissor Set(RectI rect) => new CanvasScissor(CanvasScissorKind.Set, rect);

	/// <summary>
	/// Creates a scissor to intersect the current one with <paramref name="rect"/>.
	/// </summary>
	/// <param name="rect">Scissor rect to intersect the current one with, in pixel coordinates.</param>
	/// <remarks>
	/// This is only valid for use in parameter overrides.
	/// </remarks>
	public static CanvasScissor Intersect(RectI rect) => new CanvasScissor(CanvasScissorKind.Intersect, rect);

	public bool Equals(CanvasScissor other) => Kind == other.Kind && Rect.Equals(other.Rect);
	public override bool Equals(object? obj) => obj is CanvasScissor other && Equals(other);
	public override int GetHashCode() => HashCode.Combine((int)Kind, Rect);
	public static bool operator ==(CanvasScissor left, CanvasScissor right) => left.Equals(right);
	public static bool operator !=(CanvasScissor left, CanvasScissor right) => !left.Equals(right);
}

/// <summary>
/// Describes how <see cref="Canvas"/> should render textures.
/// </summary>
public readonly struct CanvasMaterial : IEquatable<CanvasMaterial> {
	/// <summary>
	/// How sampled texture data should be interpreted.
	/// </summary>
	public required TextureInterpretation TextureInterpretation { get; init; }

	/// <summary>
	/// Parameters used when <see cref="TextureInterpretation"/> is
	/// <see cref="TextureInterpretation.SDF"/>.
	/// </summary>
	public SdfParams? SdfParams { get; init; }

	public bool Equals(CanvasMaterial other) => TextureInterpretation == other.TextureInterpretation && SdfParams == other.SdfParams;
	public override bool Equals(object? obj) => obj is CanvasMaterial other && Equals(other);
	public override int GetHashCode() => HashCode.Combine((int)TextureInterpretation, SdfParams);
	public static bool operator ==(CanvasMaterial left, CanvasMaterial right) => left.Equals(right);
	public static bool operator !=(CanvasMaterial left, CanvasMaterial right) => !left.Equals(right);
}

/// <summary>
/// Provides <see cref="CanvasMaterial"/> instances for common cases.
/// </summary>
public static class CanvasMaterials {
	/// <summary>
	/// A material that interprets textures as ordinary color data.
	/// </summary>
	public static readonly CanvasMaterial Color = new CanvasMaterial {
		TextureInterpretation = TextureInterpretation.Color
	};

	/// <summary>
	/// A material that interprets textures' red channels as coverage masks and
	/// ignores the other channels.
	/// </summary>
	public static readonly CanvasMaterial RMask = new CanvasMaterial {
		TextureInterpretation = TextureInterpretation.RMask
	};

	/// <summary>
	/// Creates a material that interprets textures as signed distance fields
	/// using the specified <see cref="SdfParams"/>.
	/// </summary>
	public static CanvasMaterial SDF(SdfParams @params) => new CanvasMaterial {
		TextureInterpretation = TextureInterpretation.SDF,
		SdfParams = @params
	};
}

/// <summary>
/// Controls how primitive and textured draws are ordered and flushed in <see cref="Canvas"/>.
/// </summary>
/// <remarks>
/// This changes rendering order; it is not merely a batching/performance hint.
/// <see cref="SwapAndFlush"/> preserves draw-call order across primitive/textured
/// transitions by flushing when the active batch type changes. The other modes
/// group work together and flush only on canvas flush, reordering visible results.
/// </remarks>
public enum CanvasSubmitMode {
	/// <summary>
	/// Accumulate primitive and textured draws separately, then submit primitives first
	/// and textured draws second when flushed.
	/// </summary>
	PrimitivesThenTextures,

	/// <summary>
	/// Accumulate primitive and textured draws separately, then submit textured draws
	/// first and primitives second when flushed.
	/// </summary>
	TexturesThenPrimitives,

	/// <summary>
	/// Preserve draw-call order by flushing whenever the active batch type changes.
	/// </summary>
	SwapAndFlush
}

/// <summary>
/// Parameters for <see cref="Canvas"/>.
/// </summary>
/// <param name="Transform">Transform matrix to be applied to draws.</param>
/// <param name="Target">Target to be drawn into.</param>
/// <param name="ColorAttachmentOps">
/// Color attachment load/store behavior used when opening/reopening the pass
/// for <paramref name="Target"/>.
/// </param>
/// <param name="Scissor">Scissor rect.</param>
/// <param name="SubmitMode">Submit policy for mixed primitive/textured draws.</param>
/// <remarks>
/// Passing a scissor of kind <see cref="CanvasScissorKind.Intersect"/> is invalid for
/// the base params.
///
/// Changing <paramref name="Target"/> or <paramref name="ColorAttachmentOps"/> is
/// pass-affecting and causes the current pass to flush and reopen. Changing
/// <paramref name="Transform"/>, <paramref name="Material"/>, or <paramref name="SubmitMode"/>
/// is batch-affecting and causes current batches to flush. Changing <paramref name="Scissor"/>,
/// while not inherently batch-affecting, causes current batches to flush before applying
/// the scissor to avoid draw calls "leaking" into the new scissor state.
/// </remarks>
public readonly record struct CanvasParams(
	// pass-affecting
	CanvasTarget Target,
	ColorAttachmentOps ColorAttachmentOps,

	// scissor (doesn't fall into either of these categories)
	CanvasScissor Scissor,

	// batch-affecting
	Matrix3x2 Transform,
	CanvasMaterial Material,
	CanvasSubmitMode SubmitMode = CanvasSubmitMode.SwapAndFlush
);

/// <summary>
/// Partial override applied on top of the current <see cref="CanvasParams"/>.
/// </summary>
/// <remarks>
/// A <see langword="null"/> field means "inherit the current value".
/// </remarks>
public readonly record struct CanvasParamsOverride(
	// pass-affecting
	CanvasTarget? Target = null,
	ColorAttachmentOps? ColorAttachmentOps = null,

	// scissor (doesn't fall into either of these categories)
	CanvasScissor? Scissor = null,

	// batch-affecting
	Matrix3x2? Transform = null,
	CanvasMaterial? Material = null,
	CanvasSubmitMode? SubmitMode = null
);

/// <summary>
/// Longer-lived, shared cache of reusable batch state for <see cref="Canvas"/> instances.
/// </summary>
/// <remarks>
/// Stores reusable batch state such as shaders and pipelines/layouts.
/// Unlike older versions, this does not own per-submit GPU buffers as they are now
/// owned by the batches. This makes it safe to share this across multiple canvases/frames.
///
/// Concurrent use is still unsafe, however, as missing cache entries are lazy-created and
/// stored in ordinary dictionaries without synchronization.
///
/// Batch state is stored per format as pipelines are color-target-format-specific.
/// </remarks>
public sealed class CanvasSharedResources(WebGPURenderer renderer, EngineResourceStore engineResources) : IDisposable {
	public readonly record struct TexBatchKey(
		TextureInterpretation TextureInterpretation,
		TextureFormat ColorTargetFormat
	);

	private readonly WebGPURenderer renderer = renderer;
	private readonly EngineResourceStore engineResources = engineResources;
	private readonly Dictionary<TextureFormat, PrimitiveBatchSharedState> primState = new Dictionary<TextureFormat, PrimitiveBatchSharedState>();
	private readonly Dictionary<TexBatchKey, TexturedBatchSharedState> texState = new Dictionary<TexBatchKey, TexturedBatchSharedState>();
	private bool disposed = false;

	/// <summary>
	/// Gets or lazy-creates primitive batch state for the given color target format.
	/// </summary>
	public PrimitiveBatchSharedState GetPrimitiveBatchSharedState(TextureFormat colorTargetFormat) {
		ObjectDisposedException.ThrowIf(disposed, this);

		if (!primState.TryGetValue(colorTargetFormat, out PrimitiveBatchSharedState? r)) {
			r = new PrimitiveBatchSharedState(renderer, engineResources, colorTargetFormat);
			primState.Add(colorTargetFormat, r);
		}
		return r;
	}

	/// <summary>
	/// Gets or lazy-creates textured batch state for the given key.
	/// </summary>
	public TexturedBatchSharedState GetTexturedBatchSharedState(TexBatchKey key) {
		ObjectDisposedException.ThrowIf(disposed, this);

		if (!texState.TryGetValue(key, out TexturedBatchSharedState? r)) {
			r = new TexturedBatchSharedState(renderer, engineResources, key.TextureInterpretation, key.ColorTargetFormat);
			texState.Add(key, r);
		}
		return r;
	}

	/// <summary>
	/// Disposes all stored batch state.
	/// </summary>
	public void Dispose() {
		if (disposed)
			return;
		disposed = true;

		foreach (TexturedBatchSharedState r in texState.Values)
			r.Dispose();
		texState.Clear();

		foreach (PrimitiveBatchSharedState r in primState.Values)
			r.Dispose();
		primState.Clear();
	}
}

/// <summary>
/// Primary 2D rendering interface for game code to draw into a <see cref="RenderFrame"/>.
/// </summary>
/// <remarks>
/// <see cref="Canvas"/> has a stack of <see cref="CanvasParams"/>. Calling
/// <see cref="PushParams(in CanvasParamsOverride)"/> pushes a new override that
/// gets popped once the returned <see cref="IDisposable"/> is disposed, creating a
/// "scoped parameters" model.
///
/// It is also responsible for managing the render passes and the primitive/textured batch
/// instances used to batch together related draw calls. Pass/batch lifetime management
/// is automatic; changing the current parameters reopens passes and flushes batches
/// as needed.
///
/// The frame is never submitted; it must be submitted manually after disposal.
/// </remarks>
public sealed class Canvas : IDisposable {
	// ==========================================================================
	// internal types
	private enum ActiveBatchType {
		None,
		Primitive,
		Textured
	}

	private sealed class ParamsScope(Canvas canvas) : IDisposable {
		private readonly Canvas canvas = canvas;
		private bool disposed = false;

		public void Dispose() {
			if (disposed)
				return;
			disposed = true;
			canvas.popParams();
		}
	}

	private sealed class Dummy : IDisposable { public void Dispose() {} }

	// ==========================================================================
	// internal objects / properties
	private readonly WebGPURenderer renderer;
	private readonly RenderFrame frame;
	private readonly CanvasSharedResources shared;
	private readonly Stack<CanvasParams> @params;

	private RenderPass? pass;
	private PrimitiveBatch? primbatch;
	private TexturedBatch? texbatch;
	private ActiveBatchType active;

	private bool disposed = false;

	private TextureFormat currentColorFormat {
		get {
			CanvasTarget t = CurrentParams.Target;
			return t.IsBackbuffer ? renderer.BackbufferFormat : t.RenderTarget.Format;
		}
	}
	
	// ==========================================================================
	// public properties and ctor

	/// <summary>
	/// Gets the currently active canvas parameters (the top of the params stack).
	/// </summary>
	public CanvasParams CurrentParams => @params.Peek();

	/// <summary>
	/// Gets the drawable width in pixels of the active target.
	/// </summary>
	public uint CurrentWidth => CurrentParams.Target.IsBackbuffer ? renderer.Width : CurrentParams.Target.RenderTarget.Width;

	/// <summary>
	/// Gets the drawable height in pixels of the active target.
	/// </summary>
	public uint CurrentHeight => CurrentParams.Target.IsBackbuffer ? renderer.Height : CurrentParams.Target.RenderTarget.Height;

	/// <summary>
	/// Creates a canvas bound to the given frame and base parameters.
	/// </summary>
	/// <param name="renderer">Renderer used to create rendering objects.</param>
	/// <param name="frame">Frame that receives all encoded work.</param>
	/// <param name="shared">Shared batch state cache to use.</param>
	/// <param name="baseParams">The base of the parameters stack.</param>
	/// <remarks>
	/// Immediately opens a render pass as dictated by <paramref name="baseParams"/>.
	///
	/// The base parameters cannot be popped off the stack.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="baseParams"/> contains a scissor of kind
	/// <see cref="CanvasScissorKind.Intersect"/>.
	/// </exception>
	public Canvas(WebGPURenderer renderer, RenderFrame frame, CanvasSharedResources shared, in CanvasParams baseParams) {
		this.renderer = renderer;
		this.frame = frame;
		this.shared = shared;
		validate(baseParams);
		@params = new Stack<CanvasParams>();
		@params.Push(baseParams);
		active = ActiveBatchType.None;
		openPass(in baseParams);
	}

	// ==========================================================================
	// primitive drawing

	/// <summary>
	/// Draws a filled triangle with interpolated per-vertex colors.
	/// </summary>
	/// <param name="a">First vertex of the triangle, in pixel coordinates.</param>
	/// <param name="b">Second vertex of the triangle, in pixel coordinates.</param>
	/// <param name="c">Third vertex of the triangle, in pixel coordinates.</param>
	public void Triangle(Vertex2DColor a, Vertex2DColor b, Vertex2DColor c) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.Triangle(a, b, c);
	}

	/// <summary>
	/// Draws a filled flat-color triangle.
	/// </summary>
	/// <param name="a">First vertex of the triangle, in pixel coordinates.</param>
	/// <param name="b">Second vertex of the triangle, in pixel coordinates.</param>
	/// <param name="c">Third vertex of the triangle, in pixel coordinates.</param>
	/// <param name="color">Fill color.</param>
	public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color32 color) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.Triangle(a, b, c, color);
	}

	/// <summary>
	/// Draws a filled axis-aligned quadrilateral with interpolated per-corner colors.
	/// </summary>
	/// <param name="topleft">Intended top-left corner of the quad, in pixel coordinates.</param>
	/// <param name="topright">Intended top-right corner of the quad, in pixel coordinates.</param>
	/// <param name="bottomleft">Intended bottom-left corner of the quad, in pixel coordinates.</param>
	/// <param name="bottomright">Intended bottom-right corner of the quad, in pixel coordinates.</param>
	/// <remarks>
	/// Concave or self-intersecting quads are invalid inputs and will produce incorrect visual output.
	/// </remarks>
	public void Quad(Vertex2DColor topleft, Vertex2DColor topright, Vertex2DColor bottomleft, Vertex2DColor bottomright) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.Quad(topleft, topright, bottomleft, bottomright);
	}

	/// <summary>
	/// Draws a filled flat-color axis-aligned quadrilateral.
	/// </summary>
	/// <param name="topleft">Intended top-left corner of the quad, in pixel coordinates.</param>
	/// <param name="topright">Intended top-right corner of the quad, in pixel coordinates.</param>
	/// <param name="bottomleft">Intended bottom-left corner of the quad, in pixel coordinates.</param>
	/// <param name="bottomright">Intended bottom-right corner of the quad, in pixel coordinates.</param>
	/// <param name="color">Fill color.</param>
	/// <remarks>
	/// Concave or self-intersecting quads are invalid inputs and will produce incorrect visual output.
	/// </remarks>
	public void Quad(Vector2 topleft, Vector2 topright, Vector2 bottomleft, Vector2 bottomright, Color32 color) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.Quad(topleft, topright, bottomleft, bottomright, color);
	}

	/// <summary>
	/// Draws a filled rectangle with interpolated per-corner colors.
	/// </summary>
	/// <param name="rect">The rectangle, in pixel coordinates.</param>
	/// <param name="cTopleft">Color for the top-left corner.</param>
	/// <param name="cTopright">Color for the top-right corner.</param>
	/// <param name="cBottomleft">Color for the bottom-left corner.</param>
	/// <param name="cBottomright">Color for the bottom-right corner.</param>
	public void Rect(RectF rect, Color32 cTopleft, Color32 cTopright, Color32 cBottomleft, Color32 cBottomright) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.Rect(rect, cTopleft, cTopright, cBottomleft, cBottomright);
	}

	/// <summary>
	/// Draws a filled flat-color rectangle.
	/// </summary>
	/// <param name="rect">The rectangle, in pixel coordinates.</param>
	/// <param name="color">Fill color.</param>
	public void Rect(RectF rect, Color32 color) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.Rect(rect, color);
	}

	/// <summary>
	/// Draws a line segment with interpolated per-endpoint colors.
	/// </summary>
	/// <param name="a">Line start point, in pixel coordinates.</param>
	/// <param name="b">Line end point, in pixel coordinates.</param>
	/// <param name="thickness">Line thickness in pixels.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is negative or zero.
	/// </exception>
	public void Line(Vertex2DColor a, Vertex2DColor b, float thickness = 1f) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.Line(a, b, thickness);
	}

	/// <summary>
	/// Draws a flat-color line segment.
	/// </summary>
	/// <param name="a">Line start point, in pixel coordinates.</param>
	/// <param name="b">Line end point, in pixel coordinates.</param>
	/// <param name="color">Line color.</param>
	/// <param name="thickness">Line thickness in pixels.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is negative or zero.
	/// </exception>
	public void Line(Vector2 a, Vector2 b, Color32 color, float thickness = 1f) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.Line(a, b, color, thickness);
	}

	/// <summary>
	/// Draws a filled convex polygon with interpolated per-vertex colors.
	/// </summary>
	/// <param name="verts">Vertices of the polygon in draw order, in pixel coordinates.</param>
	/// <remarks>
	/// The polygon must be convex and will otherwise produce incorrect visual input.
	/// </remarks>
	public void ConvexPoly(ReadOnlySpan<Vertex2DColor> verts) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.ConvexPoly(verts);
	}

	/// <summary>
	/// Draws a filled flat-color convex polygon.
	/// </summary>
	/// <param name="points">Vertices of the polygon in draw order, in pixel coordinates.</param>
	/// <param name="color">Fill color.</param>
	/// <remarks>
	/// The polygon must be convex and will otherwise produce incorrect visual input.
	/// </remarks>
	public void ConvexPoly(ReadOnlySpan<Vector2> points, Color32 color) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.ConvexPoly(points, color);
	}

	/// <summary>
	/// Draws a filled triangle list with interpolated per-vertex colors.
	/// </summary>
	/// <param name="verts">Vertices forming the triangles, in pixel coordinates.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if the amount of vertices in <paramref name="verts"/> is not a multiple of 3.
	/// </exception>
	public void TriangleList(ReadOnlySpan<Vertex2DColor> verts) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.TriangleList(verts);
	}

	/// <summary>
	/// Draws a filled flat-color triangle list.
	/// </summary>
	/// <param name="points">Vertices forming the triangles, in pixel coordinates.</param>
	/// <param name="color">Fill color.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if the amount of points in <paramref name="points"/> is not a multiple of 3.
	/// </exception>
	public void TriangleList(ReadOnlySpan<Vector2> points, Color32 color) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.TriangleList(points, color);
	}

	/// <summary>
	/// Draws a filled triangle strip with interpolated per-vertex colors.
	/// </summary>
	/// <param name="verts">Vertices forming the triangle strip, in pixel coordinates.</param>
	public void TriangleStrip(ReadOnlySpan<Vertex2DColor> verts) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.TriangleStrip(verts);
	}

	/// <summary>
	/// Draws a filled flat-color triangle strip.
	/// </summary>
	/// <param name="points">Vertices forming the triangle strip, in pixel coordinates.</param>
	/// <param name="color">Fill color.</param>
	public void TriangleStrip(ReadOnlySpan<Vector2> points, Color32 color) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.TriangleStrip(points, color);
	}

	/// <summary>
	/// Draws a filled line list with interpolated per-endpoint colors.
	/// </summary>
	/// <param name="verts">Points forming the lines, in pixel coordinates.</param>
	/// <param name="thickness">Line thickness in pixels.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if the amount of vertices in <paramref name="verts"/> is not a multiple of 2.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is negative or zero.
	/// </exception>
	public void LineList(ReadOnlySpan<Vertex2DColor> verts, float thickness = 1f) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.LineList(verts, thickness);
	}

	/// <summary>
	/// Draws a filled flat-color line list.
	/// </summary>
	/// <param name="points">Points forming the lines, in pixel coordinates.</param>
	/// <param name="color">Fill color.</param>
	/// <param name="thickness">Line thickness in pixels.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if the amount of points in <paramref name="points"/> is not a multiple of 2.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is negative or zero.
	/// </exception>
	public void LineList(ReadOnlySpan<Vector2> points, Color32 color, float thickness = 1f) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.LineList(points, color, thickness);
	}

	/// <summary>
	/// Draws a filled line strip with interpolated per-endpoint colors.
	/// </summary>
	/// <param name="verts">Points forming the line strip, in pixel coordinates.</param>
	/// <param name="thickness">Line thickness in pixels.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is negative or zero.
	/// </exception>
	public void LineStrip(ReadOnlySpan<Vertex2DColor> verts, float thickness = 1f) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.LineStrip(verts, thickness);
	}

	/// <summary>
	/// Draws a filled flat-color line strip.
	/// </summary>
	/// <param name="points">Points forming the line strip, in pixel coordinates.</param>
	/// <param name="color">Fill color.</param>
	/// <param name="thickness">Line thickness in pixels.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="thickness"/> is negative or zero.
	/// </exception>
	public void LineStrip(ReadOnlySpan<Vector2> points, Color32 color, float thickness = 1f) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ensurePrimBatch();
		primbatch.LineStrip(points, color, thickness);
	}

	// ==========================================================================
	// textured drawing
	private static readonly RectF fullUV = new RectF(0f, 0f, 1f, 1f);

	private static RectF pxToUV(in ResolvedTextureSource tex, RectF srcPixels) {
		float invW = 1f / (float)tex.Width;
		float invH = 1f / (float)tex.Height;
		return new RectF(srcPixels.X * invW, srcPixels.Y * invH, srcPixels.Width * invW, srcPixels.Height * invH);
	}

	private static RectF texdst(in ResolvedTextureSource tex, Vector2 topLeft) {
		return new RectF(topLeft.X, topLeft.Y, (float)tex.Width, (float)tex.Height);
	}

	private static RectF texdst(Vector2 topLeft, RectF srcPixels) {
		return new RectF(topLeft.X, topLeft.Y, srcPixels.Width, srcPixels.Height);
	}

	private void checkSelfDraw(in ResolvedTextureSource tex) {
		if (CurrentParams.Target.IsBackbuffer)
			return;
		if (tex.RTAndSameColorTarget(CurrentParams.Target.RenderTarget))
			throw new InvalidOperationException("attempt to draw a render target while it is the active canvas target (i.e draw it into itself)");
	}

	private void texquad(in ResolvedTextureSource tex, RectF dst, RectF uv, Color32 color) {
		checkSelfDraw(tex);
		ensureTexBatch();
		texbatch.Quad(tex, dst, uv, color);
	}

	private void resolve(TextureSource tex, Action<ResolvedTextureSource> act) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ResolvedTextureSource r = tex.Resolve();
		act(r);
	}

	// =========================
	// full texture draw

	/// <summary>
	/// Draws an entire texture at the specified position.
	/// </summary>
	/// <param name="tex">Texture to draw.</param>
	/// <param name="topleft">Destination top-left corner, in pixel coordinates.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="tex"/> is the currently active render target.
	/// </exception>
	public void Texture(TextureSource tex, Vector2 topleft) {
		resolve(tex, r => texquad(r, texdst(r, topleft), fullUV, Color32.White));
	}

	/// <summary>
	/// Draws an entire texture at the specified position, with a tint.
	/// </summary>
	/// <param name="tex">Texture to draw.</param>
	/// <param name="topleft">Destination top-left corner, in pixel coordinates.</param>
	/// <param name="color">Multiplicative tint applied after sampling.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="tex"/> is the currently active render target.
	/// </exception>
	public void Texture(TextureSource tex, Vector2 topleft, Color32 color) {
		resolve(tex, r => texquad(r, texdst(r, topleft), fullUV, color));
	}

	/// <summary>
	/// Draws an entire texture into the given destination rectangle.
	/// </summary>
	/// <param name="tex">Texture to draw.</param>
	/// <param name="dst">Destination rectangle, in pixel coordinates.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="tex"/> is the currently active render target.
	/// </exception>
	public void Texture(TextureSource tex, RectF dst) {
		resolve(tex, r => texquad(r, dst, fullUV, Color32.White));
	}

	/// <summary>
	/// Draws an entire texture into the given destination rectangle, with a tint.
	/// </summary>
	/// <param name="tex">Texture to draw.</param>
	/// <param name="dst">Destination rectangle, in pixel coordinates.</param>
	/// <param name="color">Multiplicative tint applied after sampling.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="tex"/> is the currently active render target.
	/// </exception>
	public void Texture(TextureSource tex, RectF dst, Color32 color) {
		resolve(tex, r => texquad(r, dst, fullUV, color));
	}

	// =========================
	// draw with source rect

	/// <summary>
	/// Draws a source rectangle from a texture at the specified position.
	/// </summary>
	/// <param name="tex">Texture to draw.</param>
	/// <param name="topleft">Destination top-left corner, in pixel coordinates.</param>
	/// <param name="srcPixels">Source rectangle, in pixel coordinates.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="tex"/> is the currently active render target.
	/// </exception>
	public void TexWithSourceRect(TextureSource tex, Vector2 topleft, RectF srcPixels) {
		resolve(tex, r => texquad(r, texdst(topleft, srcPixels), pxToUV(r, srcPixels), Color32.White));
	}

	/// <summary>
	/// Draws a source rectangle from a texture at the specified position, with a tint.
	/// </summary>
	/// <param name="tex">Texture to draw.</param>
	/// <param name="topleft">Destination top-left corner, in pixel coordinates.</param>
	/// <param name="srcPixels">Source rectangle, in pixel coordinates.</param>
	/// <param name="color">Multiplicative tint applied after sampling.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="tex"/> is the currently active render target.
	/// </exception>
	public void TexWithSourceRect(TextureSource tex, Vector2 topleft, RectF srcPixels, Color32 color) {
		resolve(tex, r => texquad(r, texdst(topleft, srcPixels), pxToUV(r, srcPixels), color));
	}

	/// <summary>
	/// Draws a source rectangle from a texture into the given destination rectangle.
	/// </summary>
	/// <param name="tex">Texture to draw.</param>
	/// <param name="dst">Destination rectangle, in pixel coordinates.</param>
	/// <param name="srcPixels">Source rectangle, in pixel coordinates.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="tex"/> is the currently active render target.
	/// </exception>
	public void TexWithSourceRect(TextureSource tex, RectF dst, RectF srcPixels) {
		resolve(tex, r => texquad(r, dst, pxToUV(r, srcPixels), Color32.White));
	}

	/// <summary>
	/// Draws a source rectangle from a texture into the given destination rectangle, with a tint.
	/// </summary>
	/// <param name="tex">Texture to draw.</param>
	/// <param name="dst">Destination rectangle, in pixel coordinates.</param>
	/// <param name="srcPixels">Source rectangle, in pixel coordinates.</param>
	/// <param name="color">Multiplicative tint applied after sampling.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="tex"/> is the currently active render target.
	/// </exception>
	public void TexWithSourceRect(TextureSource tex, RectF dst, RectF srcPixels, Color32 color) {
		resolve(tex, r => texquad(r, dst, pxToUV(r, srcPixels), color));
	}

	// =========================
	// draw with user-passed uv

	/// <summary>
	/// Draws a source UV rectangle from a texture into the given destination rectangle.
	/// </summary>
	/// <param name="tex">Texture to draw.</param>
	/// <param name="dst">Destination rectangle, in pixel coordinates.</param>
	/// <param name="uv">Source rectangle, in normalized UV coordinates.</param>
	/// <remarks>
	/// This is the lowest-level method for texture drawing in <see cref="Canvas"/>.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="tex"/> is the currently active render target.
	/// </exception>
	public void TexWithUVRect(TextureSource tex, RectF dst, RectF uv) {
		resolve(tex, r => texquad(r, dst, uv, Color32.White));
	}

	/// <summary>
	/// Draws a source UV rectangle from a texture into the given destination rectangle, with a tint.
	/// </summary>
	/// <param name="tex">Texture to draw.</param>
	/// <param name="dst">Destination rectangle, in pixel coordinates.</param>
	/// <param name="uv">Source rectangle, in normalized UV coordinates.</param>
	/// <param name="color">Multiplicative tint applied after sampling.</param>
	/// <remarks>
	/// This is the lowest-level method for texture drawing in <see cref="Canvas"/>.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="tex"/> is the currently active render target.
	/// </exception>
	public void TexWithUVRect(TextureSource tex, RectF dst, RectF uv, Color32 color) {
		resolve(tex, r => texquad(r, dst, uv, color));
	}

	// ==========================================================================
	// params

	/// <summary>
	/// Pushes a temporary parameter override onto the canvas parameter stack.
	/// </summary>
	/// <param name="ov">Partial override applied on top of <see cref="CurrentParams"/>.</param>
	/// <returns>
	/// An <see cref="IDisposable"/> that restores the previous parameter state
	/// when disposed.
	/// </returns>
	/// <remarks>
	/// This is the standard way to temporarily change the transform, render target,
	/// etc. This may reopen the pass / flush batches if necessary.
	/// </remarks>
	public IDisposable PushParams(in CanvasParamsOverride ov) {
		ObjectDisposedException.ThrowIf(disposed, this);
		CanvasParams old = CurrentParams;
		CanvasParams @new = merge(in old, in ov);
		validate(@new);
		transition(in old, in @new, returning: false);
		@params.Push(@new);
		return new ParamsScope(this);
	}

	/// <summary>
	/// Pushes a temporary parameter override onto the canvas parameter stack.
	/// </summary>
	/// <returns>
	/// An <see cref="IDisposable"/> that restores the previous parameter state
	/// when disposed.
	/// </returns>
	/// <remarks>
	/// This is the standard way to temporarily change the transform, render target,
	/// etc. This may reopen the pass / flush batches if necessary.
	///
	/// This is a convenience overload so that you don't have to type
	/// <c>new CanvasParamsOverride(</c> every time. The parameters mirror the
	/// values in <see cref="CanvasParamsOverride"/>, this overload creates
	/// one from them and passes it to the plain overload.
	/// </remarks>
	public IDisposable PushParams(
		CanvasTarget? Target = null,
		ColorAttachmentOps? ColorAttachmentOps = null,
		CanvasScissor? Scissor = null,
		Matrix3x2? Transform = null,
		CanvasMaterial? Material = null,
		CanvasSubmitMode? SubmitMode = null
	) => PushParams(new CanvasParamsOverride(Target, ColorAttachmentOps, Scissor, Transform, Material, SubmitMode));

	/// <summary>
	/// Pushes a temporary parameter override onto the canvas parameter stack, or
	/// no-ops if the parameter override is <see langword="null"/>.
	/// </summary>
	/// <param name="ov">
	/// Partial override applied on top of <see cref="CurrentParams"/>, or
	/// <see langword="null"/> for a no-op.
	/// </param>
	/// <returns>
	/// An <see cref="IDisposable"/> that restores the previous parameter state
	/// when disposed. If <paramref name="ov"/> is <see langword="null"/>, returns
	/// a dummy object that does nothing instead.
	/// </returns>
	/// <remarks>
	/// This is a convenience method to allow APIs to return <c>CanvasParamsOverride?</c>,
	/// for example <c>using (cv.PushParamsIfNonnull(foo.GetCanvasParams(bar, baz))) { ... }</c>.
	/// </remarks>
	public IDisposable PushParamsIfNonnull(in CanvasParamsOverride? ov) {
		if (ov is CanvasParamsOverride nonnull)
			return PushParams(in nonnull);
		return new Dummy();
	}

	private void popParams() {
		// this gets called from ParamsScope.Dispose, which is public, so
		// treat this as a public method
		ObjectDisposedException.ThrowIf(disposed, this);
		if (@params.Count <= 1)
			throw new InternalStateException("ParamsScope dispose tried to pop base canvas params off the stack");
		CanvasParams old = @params.Pop();
		CanvasParams @new = CurrentParams;
		transition(in old, in @new, returning: true);
	}

	private static CanvasParams merge(in CanvasParams curr, in CanvasParamsOverride ov) {
		return new CanvasParams(
			Transform: ov.Transform ?? curr.Transform,
			Target: ov.Target ?? curr.Target,
			ColorAttachmentOps: ov.ColorAttachmentOps ?? curr.ColorAttachmentOps,
			Scissor: mergeScissor(curr.Scissor, ov.Scissor),
			Material: ov.Material ?? curr.Material,
			SubmitMode: ov.SubmitMode ?? curr.SubmitMode
		);
	}
	
	private void transition(in CanvasParams from, in CanvasParams to, bool returning) {
		bool newtarget = from.Target != to.Target;
		bool newattops = from.ColorAttachmentOps != to.ColorAttachmentOps;
		bool affectspass = newtarget || newattops;

		bool newscissor = from.Scissor != to.Scissor;

		bool newtransform = from.Transform != to.Transform;
		bool newmaterial = from.Material != to.Material;
		bool newsubmit = from.SubmitMode != to.SubmitMode;
		bool affectsbatch = newtransform || newmaterial || newsubmit;

		if (pass is null)
			throw new InternalStateException("was expecting an open render pass to be there for params transition");
		if (affectspass) {
			flush(from.SubmitMode);
			closePass();
			openPass(newtarget && returning ? (to with { ColorAttachmentOps = ColorAttachmentOps.Load }) : to);
		} else if (affectsbatch || newscissor) {
			flush(from.SubmitMode);
			if (newscissor)
				applyScissor(pass, in to);
		}
	}

	private static void validate(in CanvasParams p) {
		if (p.Scissor.Kind == CanvasScissorKind.Intersect)
			throw new ArgumentException("CanvasParams.Scissor cannot be Intersect as there is no existing scissor to intersect with");
		if ((p.Scissor.Kind == CanvasScissorKind.Set || p.Scissor.Kind == CanvasScissorKind.Intersect) &&
			(p.Scissor.Rect.Width < 0 || p.Scissor.Rect.Height < 0))
			throw new ArgumentException("scissor rect cannot have negative width/height");
		if (p.Material.TextureInterpretation == TextureInterpretation.SDF && p.Material.SdfParams is null)
			throw new ArgumentException("CanvasParams.Material.SdfParams must be set if the texture interpretation of the material is SDF");
	}

	// ==========================================================================
	// scissor
	private static RectI intersect(RectI a, RectI b) {
		int x1 = Math.Max(a.X, b.X);
		int y1 = Math.Max(a.Y, b.Y);
		int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
		int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
		x2 = Math.Max(x1, x2);
		y2 = Math.Max(y1, y2);
		return new RectI(x1, y1, x2 - x1, y2 - y1);
	}

	private static CanvasScissor mergeScissor(CanvasScissor curr, CanvasScissor? ov) {
		if (ov is not CanvasScissor s)
			return curr;
		if (curr.Kind is CanvasScissorKind.Intersect)
			throw new InternalStateException("Intersect scissor made it to mergeScissor");
		return s.Kind switch {
			CanvasScissorKind.None => CanvasScissor.None,
			CanvasScissorKind.Set => CanvasScissor.Set(s.Rect),
			CanvasScissorKind.Intersect => curr.Kind switch {
				CanvasScissorKind.None => CanvasScissor.Set(s.Rect),
				CanvasScissorKind.Set => CanvasScissor.Set(intersect(curr.Rect, s.Rect)),
				_ => throw new UnreachableException()
			},
			_ => throw new UnreachableException()
		};
	}

	private void applyScissor(RenderPass pass, in CanvasParams p) {
		// XXX: yucky casts back and forth from int/uint
		int w = (int)(p.Target.IsBackbuffer ? renderer.Width : p.Target.RenderTarget.Width);
		int h = (int)(p.Target.IsBackbuffer ? renderer.Height : p.Target.RenderTarget.Height);
		RectI full = new RectI(0, 0, w, h);
		RectI effective = p.Scissor.Kind switch {
			CanvasScissorKind.None => full,
			CanvasScissorKind.Set => intersect(full, p.Scissor.Rect),
			CanvasScissorKind.Intersect =>
				throw new InternalStateException("Intersect scissor made it to applyScissor"),
			_ => throw new UnreachableException()
		};
		pass.SetScissorRect((uint)effective.X, (uint)effective.Y, (uint)effective.Width, (uint)effective.Height);
	}

	// ==========================================================================
	// pass management
	private void openPass(in CanvasParams p) {
		if (pass is not null)
			throw new InternalStateException("tried to open a render pass but there's already an active one");

		// see above on indirection
		pass = p.Target.IsBackbuffer ?
			frame.BeginBackbufferPass(p.ColorAttachmentOps) :
			frame.BeginRenderTargetPass(p.Target.RenderTarget.Target, p.ColorAttachmentOps);
		applyScissor(pass, in p);
	}

	private void closePass() {
		if (pass is null)
			throw new InternalStateException("tried to close the active render pass but there isn't one");

		pass.Dispose();
		pass = null;
	}

	// ==========================================================================
	// batch management
	private PrimitiveBatch createPrimBatch() {
		if (pass is null)
			throw new InternalStateException("tried to create a PrimitiveBatch but there's no active render pass");
		return new PrimitiveBatch(renderer, frame, pass, shared.GetPrimitiveBatchSharedState(currentColorFormat),
			new PrimitiveBatchParams(Transform: CurrentParams.Transform));
	}

	[MemberNotNull(nameof(primbatch))]
	private void ensurePrimBatch() {
		switch (CurrentParams.SubmitMode) {
		case CanvasSubmitMode.PrimitivesThenTextures:
		case CanvasSubmitMode.TexturesThenPrimitives:
			primbatch ??= createPrimBatch();
			break;
		case CanvasSubmitMode.SwapAndFlush:
			if (active != ActiveBatchType.Primitive) {
				flushActiveBatch();
				active = ActiveBatchType.Primitive;
			}
			primbatch ??= createPrimBatch();
			break;
		default:
			throw new UnreachableException();
		}
	}

	private TexturedBatch createTexBatch() {
		if (pass is null)
			throw new InternalStateException("tried to create a TexturedBatch but there's no active render pass");
		return new TexturedBatch(renderer, frame, pass,
			shared.GetTexturedBatchSharedState(new CanvasSharedResources.TexBatchKey(CurrentParams.Material.TextureInterpretation, currentColorFormat)),
			new TexturedBatchParams(Transform: CurrentParams.Transform, SdfParams: CurrentParams.Material.SdfParams));
	}

	[MemberNotNull(nameof(texbatch))]
	private void ensureTexBatch() {
		switch (CurrentParams.SubmitMode) {
		case CanvasSubmitMode.PrimitivesThenTextures:
		case CanvasSubmitMode.TexturesThenPrimitives:
			texbatch ??= createTexBatch();
			break;
		case CanvasSubmitMode.SwapAndFlush:
			if (active != ActiveBatchType.Textured) {
				flushActiveBatch();
				active = ActiveBatchType.Textured;
			}
			texbatch ??= createTexBatch();
			break;
		default:
			throw new UnreachableException();
		}
	}

	// ==========================================================================
	// flush / dispose
	private void flushPrimBatch() {
		if (primbatch is null)
			return;
		primbatch.Submit();
		primbatch.Dispose();
		primbatch = null;
		if (active == ActiveBatchType.Primitive)
			active = ActiveBatchType.None;
	}

	private void flushTexBatch() {
		if (texbatch is null)
			return;
		texbatch.Submit();
		texbatch.Dispose();
		texbatch = null;
		if (active == ActiveBatchType.Textured)
			active = ActiveBatchType.None;
	}

	private void flushActiveBatch() {
		switch (active) {
		case ActiveBatchType.None:
			break;
		case ActiveBatchType.Primitive:
			flushPrimBatch();
			break;
		case ActiveBatchType.Textured:
			flushTexBatch();
			break;
		}
	}

	private void flush(CanvasSubmitMode mode) {
		switch (mode) {
		case CanvasSubmitMode.PrimitivesThenTextures:
			flushPrimBatch();
			flushTexBatch();
			break;
		case CanvasSubmitMode.TexturesThenPrimitives:
			flushTexBatch();
			flushPrimBatch();
			break;
		case CanvasSubmitMode.SwapAndFlush:
			flushActiveBatch();
			break;
		}
		if (primbatch is not null || texbatch is not null)
			throw new InternalStateException("batch survived flush");
	}

	/// <summary>
	/// Flushes all currently accumulated draws according to the active submit mode.
	/// </summary>
	/// <remarks>
	/// This does not end the active pass or submit the frame.
	/// </remarks>
	public void Flush() {
		ObjectDisposedException.ThrowIf(disposed, this);
		flush(CurrentParams.SubmitMode);
	}

	/// <summary>
	/// Flushes all currently accumulated draws, ends the active pass, and finalizes,
	/// not allowing further draws/etc.
	/// </summary>
	/// <remarks>
	/// This does not submit the frame.
	/// </remarks>
	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		flush(CurrentParams.SubmitMode);
		closePass();
	}
}
