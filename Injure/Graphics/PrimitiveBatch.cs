// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using Silk.NET.WebGPU;

using Injure.Assets;
using Injure.Assets.Builtin;
using Injure.Rendering;

namespace Injure.Graphics;

[StructLayout(LayoutKind.Sequential)]
public struct PrimitiveBatchLocalsUniform {
	public required Matrix4x4 Transform;

	public static readonly int Size = Unsafe.SizeOf<PrimitiveBatchLocalsUniform>();
}

public sealed class PrimitiveBatchSharedState : IDisposable {
	public readonly TextureFormat ColorTargetFormat;
	private readonly GPUShader _shader;
	private readonly GPUBindGroupLayout _localsBindGroupLayout;
	private readonly GPUPipelineLayout _pipelineLayout;
	private readonly GPURenderPipeline _pipeline;
	private bool disposed = false;

	public GPUShader Shader { get { ObjectDisposedException.ThrowIf(disposed, this); return _shader; } }
	public GPUBindGroupLayoutRef LocalsBindGroupLayout { get { ObjectDisposedException.ThrowIf(disposed, this); return _localsBindGroupLayout.AsRef(); } }
	public GPUPipelineLayout PipelineLayout { get { ObjectDisposedException.ThrowIf(disposed, this); return _pipelineLayout; } }
	public GPURenderPipeline Pipeline { get { ObjectDisposedException.ThrowIf(disposed, this); return _pipeline; } }

	public PrimitiveBatchSharedState(WebGPUDevice device, EngineResourceStore engineResources, BlendState? blend,
		ColorWriteMask colorWriteMask, TextureFormat colorTargetFormat) {
		ColorTargetFormat = colorTargetFormat;
		_shader = device.CreateShaderWGSL(engineResources.GetText(BuiltinShaders.Primitive2D.ResourceID));
		_localsBindGroupLayout = device.CreateSimpleBufferBindGroupLayout(ShaderStage.Vertex, (ulong)PrimitiveBatchLocalsUniform.Size);
		_pipelineLayout = device.CreatePipelineLayout([
			device.GlobalsUniformBindGroupLayout,
			_localsBindGroupLayout
		]);
		_pipeline = device.CreateRenderPipeline(PipelineLayout, new GPURenderPipelineCreateParams(
			Shader: Shader,
			VertShaderEntryPoint: BuiltinShaders.Primitive2D.VSEntry,
			FragShaderEntryPoint: BuiltinShaders.Primitive2D.FSEntry,
			VertexStride: (ulong)Vertex2DColor.Size,
			VertexStepMode: VertexStepMode.Vertex,
			VertexAttributes: [
				new VertexAttribute {
					Format = VertexFormat.Float32x2,
					Offset = 0,
					ShaderLocation = 0
				},
				new VertexAttribute {
					Format = VertexFormat.Unorm8x4,
					Offset = 8,
					ShaderLocation = 1
				}
			],
			PrimitiveTopology: PrimitiveTopology.TriangleList,
			FrontFace: FrontFace.Ccw,
			CullMode: CullMode.None,
			ColorTargetFormat: colorTargetFormat,
			Blend: blend,
			ColorWriteMask: colorWriteMask
		));
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		_pipeline.Dispose();
		_pipelineLayout.Dispose();
		_localsBindGroupLayout.Dispose();
		_shader.Dispose();
	}
}

public readonly record struct PrimitiveBatchParams(
	Matrix3x2 Transform
);

// policy: ccw winding for generated geometry, preserve existing order for user-passed geometry
public sealed class PrimitiveBatch : IDisposable {
	private readonly WebGPUDevice device;
	private readonly ViewGlobals globals;
	private readonly RenderFrame frame;
	private readonly RenderPass pass;
	private readonly PrimitiveBatchSharedState shared;
	private readonly GPUBuffer localsUniformBuffer;
	private readonly GPUBindGroup localsUniformBindGroup;

	private Vertex2DColor[] verts;
	private uint[] idxs;
	private GPUBuffer vbuffer;
	private GPUBuffer ibuffer;

	private int vcount = 0;
	private int icount = 0;

	private bool submitted = false;
	private bool disposed = false;

	public PrimitiveBatch(WebGPUDevice device, ViewGlobals globals, RenderFrame frame, RenderPass pass,
		PrimitiveBatchSharedState shared, in PrimitiveBatchParams @params, int initialVertCapacity = 256, int initialIndexCapacity = 512) {
		this.device = device;
		this.globals = globals;
		this.frame = frame;
		this.pass = pass;
		this.shared = shared;

		localsUniformBuffer = device.CreateBuffer((ulong)PrimitiveBatchLocalsUniform.Size, BufferUsage.Uniform | BufferUsage.CopyDst);
		PrimitiveBatchLocalsUniform l = new PrimitiveBatchLocalsUniform {
			Transform = MatrixUtil.To4x4(@params.Transform)
		};
		device.WriteToBuffer(localsUniformBuffer, 0, in l);
		localsUniformBindGroup = device.CreateBufferBindGroup(shared.LocalsBindGroupLayout, 0, localsUniformBuffer, 0, (ulong)PrimitiveBatchLocalsUniform.Size);

		verts = new Vertex2DColor[initialVertCapacity];
		idxs = new uint[initialIndexCapacity];
		vbuffer = device.CreateBuffer((ulong)(initialVertCapacity * Vertex2DColor.Size), BufferUsage.Vertex | BufferUsage.CopyDst);
		ibuffer = device.CreateBuffer((ulong)(initialIndexCapacity * sizeof(uint)), BufferUsage.Index | BufferUsage.CopyDst);
	}

	private void chk() {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (submitted)
			throw new InvalidOperationException("PrimitiveBatch has already been submitted");
	}

	private void ensure(int needVerts, int needIdxs) {
		if (vcount + needVerts > verts.Length) {
			int sz = Math.Max(vcount + needVerts, verts.Length * 2);
			Array.Resize(ref verts, sz);
			vbuffer.Dispose();
			vbuffer = device.CreateBuffer((ulong)(sz * Vertex2DColor.Size), BufferUsage.Vertex | BufferUsage.CopyDst);
		}
		if (icount + needIdxs > idxs.Length) {
			int sz = Math.Max(icount + needIdxs, idxs.Length * 2);
			Array.Resize(ref idxs, sz);
			ibuffer.Dispose();
			ibuffer = device.CreateBuffer((ulong)(sz * sizeof(uint)), BufferUsage.Index | BufferUsage.CopyDst);
		}
	}

	private uint addvert(in Vertex2DColor v) {
		verts[vcount] = v;
		return (uint)vcount++;
	}

	private void addverts(ReadOnlySpan<Vertex2DColor> verts) {
		for (int i = 0; i < verts.Length; i++)
			addvert(verts[i]);
	}

	private void add3idxs(uint i0, uint i1, uint i2) {
		idxs[icount++] = i0;
		idxs[icount++] = i1;
		idxs[icount++] = i2;
	}

	private static float winding(Vector2 a, Vector2 b, Vector2 c) {
		// in cartesian coordinates (+Y = up):
		// > 0 = ccw
		// < 0 = cw
		// in screen space coords, +Y is down, but i'm like pretty sure that's just the
		// ortho projection and +Y in clip space coords points up so >0 = ccw / <0 = cw
		return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
	}

	public void Triangle(Vertex2DColor a, Vertex2DColor b, Vertex2DColor c) {
		chk();
		ensure(3, 3);
		uint i0 = addvert(a);
		uint i1 = addvert(b);
		uint i2 = addvert(c);
		add3idxs(i0, i1, i2);
	}
	public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color32 color) =>
		Triangle(new Vertex2DColor(a, color), new Vertex2DColor(b, color), new Vertex2DColor(c, color));

	public void Quad(Vertex2DColor topleft, Vertex2DColor topright, Vertex2DColor bottomleft, Vertex2DColor bottomright) {
		chk();
		ensure(4, 6);
		uint i0 = addvert(topleft);
		uint i1 = addvert(topright);
		uint i2 = addvert(bottomleft);
		uint i3 = addvert(bottomright);
		add3idxs(i0, i2, i1);
		add3idxs(i3, i1, i2);
	}
	public void Quad(Vector2 topleft, Vector2 topright, Vector2 bottomleft, Vector2 bottomright, Color32 color) =>
		Quad(
			new Vertex2DColor(topleft, color),
			new Vertex2DColor(topright, color),
			new Vertex2DColor(bottomleft, color),
			new Vertex2DColor(bottomright, color)
		);

	public void Rect(RectF rect, Color32 cTopleft, Color32 cTopright, Color32 cBottomleft, Color32 cBottomright) =>
		Quad(
			new Vertex2DColor(rect.X,              rect.Y,               cTopleft),
			new Vertex2DColor(rect.X + rect.Width, rect.Y,               cTopright),
			new Vertex2DColor(rect.X,              rect.Y + rect.Height, cBottomleft),
			new Vertex2DColor(rect.X + rect.Width, rect.Y + rect.Height, cBottomright)
		);
	public void Rect(RectF rect, Color32 color) =>
		Rect(rect, color, color, color, color);

	public void Line(Vertex2DColor a, Vertex2DColor b, float thickness = 1f) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thickness);
		chk();

		const float epsilon = 0.0001f;
		Vector2 direction = (Vector2)b - (Vector2)a;
		float len = direction.Length();
		if (len < epsilon)
			return;
		direction /= len;
		Vector2 normal = new Vector2(-direction.Y, direction.X) * thickness * 0.5f;

		Vector2 p0 = (Vector2)a + normal;
		Vector2 p1 = (Vector2)b + normal;
		Vector2 p2 = (Vector2)b - normal;
		Vector2 p3 = (Vector2)a - normal;
		
		ensure(4, 6);
		uint i0 = addvert(new Vertex2DColor(p0.X, p0.Y, a.Color));
		uint i1 = addvert(new Vertex2DColor(p1.X, p1.Y, b.Color));
		uint i2 = addvert(new Vertex2DColor(p2.X, p2.Y, b.Color));
		uint i3 = addvert(new Vertex2DColor(p3.X, p3.Y, a.Color));
		if (winding(p0, p1, p2) > 0f) {
			add3idxs(i0, i2, i1);
			add3idxs(i0, i3, i2);
		} else {
			add3idxs(i0, i1, i2);
			add3idxs(i0, i2, i3);
		}
	}
	public void Line(Vector2 a, Vector2 b, Color32 color, float thickness = 1f) =>
		Line(new Vertex2DColor(a, color), new Vertex2DColor(b, color), thickness);

	private void flatcolor(Action<ReadOnlySpan<Vertex2DColor>> draw, ReadOnlySpan<Vector2> points, Color32 color) {
		const int maxstack = 256;
		Span<Vertex2DColor> verts = points.Length <= maxstack ?
			stackalloc Vertex2DColor[points.Length] :
			new Vertex2DColor[points.Length];
		for (int i = 0; i < points.Length; i++)
			verts[i] = new Vertex2DColor(points[i], color);
		draw(verts);
	}

	private void flatcolor(Action<ReadOnlySpan<Vertex2DColor>, float> draw, ReadOnlySpan<Vector2> points, Color32 color, float thickness) {
		const int maxstack = 256;
		Span<Vertex2DColor> verts = points.Length <= maxstack ?
			stackalloc Vertex2DColor[points.Length] :
			new Vertex2DColor[points.Length];
		for (int i = 0; i < points.Length; i++)
			verts[i] = new Vertex2DColor(points[i], color);
		draw(verts, thickness);
	}

	public void ConvexPoly(ReadOnlySpan<Vertex2DColor> verts) {
		// basic triangle fan from vert 0
		chk();
		if (verts.Length < 3)
			return;
		ensure(verts.Length, (verts.Length - 2) * 3);
		uint baseidx = (uint)vcount;
		addverts(verts);
		for (uint i = 1; i < (uint)verts.Length - 1; i++)
			add3idxs(baseidx, baseidx + i, baseidx + i + 1);
	}
	public void ConvexPoly(ReadOnlySpan<Vector2> points, Color32 color) =>
		flatcolor(ConvexPoly, points, color);

	public void TriangleList(ReadOnlySpan<Vertex2DColor> verts) {
		chk();
		if (verts.Length == 0)
			return;
		if ((verts.Length % 3) != 0)
			throw new ArgumentException("triangle list vertex count must be a multiple of 3", nameof(verts));
		ensure(verts.Length, verts.Length);
		uint baseidx = (uint)vcount;
		addverts(verts);
		for (uint i = 0; i < (uint)verts.Length; i += 3)
			add3idxs(baseidx + i, baseidx + i + 1, baseidx + i + 2);
	}
	public void TriangleList(ReadOnlySpan<Vector2> points, Color32 color) =>
		flatcolor(TriangleList, points, color);

	public void TriangleStrip(ReadOnlySpan<Vertex2DColor> verts) {
		chk();
		if (verts.Length < 3)
			return;
		ensure(verts.Length, (verts.Length - 2) * 3);
		uint baseidx = (uint)vcount;
		addverts(verts);
		// flip winding every other triangle
		for (uint i = 0; i < (uint)verts.Length - 2; i++) {
			if ((i & 1) == 0)
				add3idxs(baseidx + i, baseidx + i + 1, baseidx + i + 2);
			else
				add3idxs(baseidx + i + 1, baseidx + i, baseidx + i + 2);
		}
	}
	public void TriangleStrip(ReadOnlySpan<Vector2> points, Color32 color) =>
		flatcolor(TriangleStrip, points, color);

	public void LineList(ReadOnlySpan<Vertex2DColor> verts, float thickness = 1f) {
		chk();
		if (verts.Length == 0)
			return;
		if ((verts.Length % 2) != 0)
			throw new ArgumentException("line list vertex count must be a multiple of 2", nameof(verts));
		for (int i = 0; i < verts.Length; i += 2)
			Line(verts[i], verts[i + 1], thickness);
	}
	public void LineList(ReadOnlySpan<Vector2> points, Color32 color, float thickness = 1f) =>
		flatcolor(LineList, points, color, thickness);

	public void LineStrip(ReadOnlySpan<Vertex2DColor> verts, float thickness = 1f) {
		chk();
		if (verts.Length < 2)
			return;
		for (int i = 0; i < verts.Length - 1; i++)
			Line(verts[i], verts[i + 1], thickness);
	}
	public void LineStrip(ReadOnlySpan<Vector2> points, Color32 color, float thickness = 1f) =>
		flatcolor(LineStrip, points, color, thickness);

	public void Submit() {
		chk();
		submitted = true;

		if (icount == 0)
			return;
		ulong vbytes = (ulong)(vcount * Vertex2DColor.Size);
		ulong ibytes = (ulong)(icount * sizeof(uint));
		device.WriteToBuffer(vbuffer, 0, verts.AsSpan(0, vcount));
		device.WriteToBuffer(ibuffer, 0, idxs.AsSpan(0, icount));
		pass.SetPipeline(shared.Pipeline);
		pass.SetBindGroup(0, globals.BindGroup);
		pass.SetBindGroup(1, localsUniformBindGroup);
		pass.SetVertexBuffer(0, vbuffer, 0, vbytes);
		pass.SetIndexBuffer(ibuffer, IndexFormat.Uint32, 0, ibytes);
		pass.DrawIndexed((uint)icount);
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		frame.DisposeAfterSubmit(ibuffer);
		frame.DisposeAfterSubmit(vbuffer);
		frame.DisposeAfterSubmit(localsUniformBindGroup);
		frame.DisposeAfterSubmit(localsUniformBuffer);
	}
}
