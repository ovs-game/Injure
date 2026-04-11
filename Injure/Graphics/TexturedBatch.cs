// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;

using Injure.Assets;
using Injure.Assets.Builtin;
using Injure.Rendering;

namespace Injure.Graphics;

/// <summary>
/// Describes how <see cref="TexturedBatch"/> should interpret sampled texture data.
/// </summary>
public enum TextureInterpretation {
	/// <summary>
	/// Interprets the texture as ordinary color data.
	/// </summary>
	Color,

	/// <summary>
	/// Interprets the texture's red channel as a coverage mask.
	/// </summary>
	RMask,

	/// <summary>
	/// Interprets the texture as a signed distance field.
	/// </summary>
	/// <remarks>
	/// Typically, whatever is exposing a <see cref="TextureInterpretation"/> enum
	/// parameter will also expose some way to pass in a <see cref="SdfParams"/> struct,
	/// as setting those parameters is required for this interpretation mode.
	/// </remarks>
	SDF
}

/// <summary>
/// Parameters controlling signed distance field texture decoding.
/// </summary>
public readonly record struct SdfParams(
	float DistanceRangeTexels,
	float EdgeValue = 0.5f,
	float SoftnessPixels = 1.0f,
	float OutlineWidthPixels = 0.0f,
	Color32 OutlineColor = default
);

[StructLayout(LayoutKind.Sequential)]
public struct TexturedBatchLocalsUniformPlain {
	public required Matrix4x4 Transform;

	public static readonly int Size = Unsafe.SizeOf<TexturedBatchLocalsUniformPlain>();
}

[StructLayout(LayoutKind.Sequential)]
public struct TexturedBatchLocalsUniformSDF {
	public required Matrix4x4 Transform;
	public float DistanceRangeTexels;
	public float EdgeValue;
	public float SoftnessPixels;
	public float OutlineWidthPixels;
	public Vector4 OutlineColor;

	public static readonly int Size = Unsafe.SizeOf<TexturedBatchLocalsUniformSDF>();
}

public sealed class TexturedBatchSharedState : IDisposable {
	public readonly TextureInterpretation TextureInterpretation;
	public readonly TextureFormat ColorTargetFormat;
	private readonly GPUShaderModule _shader;
	private readonly GPUBindGroupLayout _localsBindGroupLayout;
	private readonly GPUPipelineLayout _pipelineLayout;
	private readonly GPURenderPipeline _pipeline;
	private bool disposed = false;

	public GPUShaderModule Shader { get { ObjectDisposedException.ThrowIf(disposed, this); return _shader; } }
	public GPUBindGroupLayoutRef LocalsBindGroupLayout { get { ObjectDisposedException.ThrowIf(disposed, this); return _localsBindGroupLayout.AsRef(); } }
	public GPUPipelineLayout PipelineLayout { get { ObjectDisposedException.ThrowIf(disposed, this); return _pipelineLayout; } }
	public GPURenderPipeline Pipeline { get { ObjectDisposedException.ThrowIf(disposed, this); return _pipeline; } }

	public TexturedBatchSharedState(WebGPUDevice device, EngineResourceStore engineResources, BlendState? blend,
		ColorWriteMask colorWriteMask, TextureInterpretation interp, TextureFormat colorTargetFormat) {
		TextureInterpretation = interp;
		ColorTargetFormat = colorTargetFormat;
		BuiltinShaderInfo shaderInfo = interp switch {
			TextureInterpretation.Color => BuiltinShaders.Textured2DColor,
			TextureInterpretation.RMask => BuiltinShaders.Textured2DRMask,
			TextureInterpretation.SDF => BuiltinShaders.Textured2DSDF,
			_ => throw new UnreachableException()
		};
		_shader = device.CreateShaderModuleWGSL(engineResources.GetText(shaderInfo.ResourceID));
		if (interp != TextureInterpretation.SDF)
			_localsBindGroupLayout = device.CreateUniformBufferBindGroupLayout(ShaderStage.Vertex, (ulong)TexturedBatchLocalsUniformPlain.Size);
		else
			_localsBindGroupLayout = device.CreateUniformBufferBindGroupLayout(ShaderStage.Vertex | ShaderStage.Fragment, (ulong)TexturedBatchLocalsUniformSDF.Size);
		_pipelineLayout = device.CreatePipelineLayout([
			device.StdGlobalsUniformLayout,
			_localsBindGroupLayout,
			device.StdColorTexture2DLayout
		]);
		_pipeline = device.CreateRenderPipeline(PipelineLayout, new GPURenderPipelineCreateParams(
			ShaderModule: Shader,
			VertShaderEntryPoint: shaderInfo.VSEntry,
			FragShaderEntryPoint: shaderInfo.FSEntry,
			VertexStride: (ulong)Vertex2DTextureColor.Size,
			VertexStepMode: VertexStepMode.Vertex,
			VertexAttributes: [
				new VertexAttribute {
					Format = VertexFormat.Float32x2,
					Offset = 0,
					ShaderLocation = 0
				},
				new VertexAttribute {
					Format = VertexFormat.Float32x2,
					Offset = 8,
					ShaderLocation = 1
				},
				new VertexAttribute {
					Format = VertexFormat.Unorm8x4,
					Offset = 16,
					ShaderLocation = 2
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

public readonly record struct TexturedBatchParams(
	Matrix3x2 Transform,
	SdfParams? SdfParams = null
);

// policy: ccw winding for generated geometry
public sealed class TexturedBatch : IDisposable {
	private struct Run {
		public object Identity;
		public GPUBindGroupRef BindGroup;
		public uint FirstIndex;
		public uint IndexCount;
	}

	private readonly WebGPUDevice device;
	private readonly ViewGlobals globals;
	private readonly RenderFrame frame;
	private readonly RenderPass pass;
	private readonly TexturedBatchSharedState shared;
	private readonly GPUBuffer localsUniformBuffer;
	private readonly GPUBindGroup localsUniformBindGroup;

	private Vertex2DTextureColor[] verts;
	private uint[] idxs;
	private Run[] runs;
	private GPUBuffer vbuffer;
	private GPUBuffer ibuffer;

	private int vcount = 0;
	private int icount = 0;
	private int rcount = 0;

	private bool submitted = false;
	private bool disposed = false;

	public TexturedBatch(WebGPUDevice device, ViewGlobals globals, RenderFrame frame, RenderPass pass,
		TexturedBatchSharedState shared, in TexturedBatchParams @params, int initialVertCapacity = 256, int initialIndexCapacity = 512, int initialRunCapacity = 32) {
		this.device = device;
		this.globals = globals;
		this.frame = frame;
		this.pass = pass;
		this.shared = shared;

		if (shared.TextureInterpretation != TextureInterpretation.SDF) {
			TexturedBatchLocalsUniformPlain l = new TexturedBatchLocalsUniformPlain {
				Transform = MatrixUtil.To4x4(@params.Transform)
			};
			localsUniformBuffer = device.CreateBuffer((ulong)TexturedBatchLocalsUniformPlain.Size, BufferUsage.Uniform | BufferUsage.CopyDst);
			device.WriteToBuffer(localsUniformBuffer, 0, in l);
		} else {
			if (@params.SdfParams is not SdfParams p)
				throw new ArgumentNullException(nameof(@params), "TexturedBatchSharedState has SDF texture interpretation but SdfParams is null");
			TexturedBatchLocalsUniformSDF l = new TexturedBatchLocalsUniformSDF {
				Transform = MatrixUtil.To4x4(@params.Transform),
				DistanceRangeTexels = p.DistanceRangeTexels,
				EdgeValue = p.EdgeValue,
				SoftnessPixels = p.SoftnessPixels,
				OutlineWidthPixels = p.OutlineWidthPixels,
				OutlineColor = p.OutlineColor.ToVector4()
			};
			localsUniformBuffer = device.CreateBuffer((ulong)TexturedBatchLocalsUniformSDF.Size, BufferUsage.Uniform | BufferUsage.CopyDst);
			device.WriteToBuffer(localsUniformBuffer, 0, in l);
		}
		localsUniformBindGroup = device.CreateUniformBufferBindGroup(shared.LocalsBindGroupLayout, localsUniformBuffer);
		verts = new Vertex2DTextureColor[initialVertCapacity];
		idxs = new uint[initialIndexCapacity];
		runs = new Run[initialRunCapacity];
		vbuffer = device.CreateBuffer((ulong)(initialVertCapacity * Vertex2DTextureColor.Size), BufferUsage.Vertex | BufferUsage.CopyDst);
		ibuffer = device.CreateBuffer((ulong)(initialIndexCapacity * sizeof(uint)), BufferUsage.Index | BufferUsage.CopyDst);
	}

	private void chk() {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (submitted)
			throw new InvalidOperationException("TexturedBatch has already been submitted");
	}

	private void ensure(int needVerts, int needIdxs, int needRuns) {
		if (vcount + needVerts > verts.Length) {
			int sz = Math.Max(vcount + needVerts, verts.Length * 2);
			Array.Resize(ref verts, sz);
			vbuffer.Dispose();
			vbuffer = device.CreateBuffer((ulong)(sz * Vertex2DTextureColor.Size), BufferUsage.Vertex | BufferUsage.CopyDst);
		}
		if (icount + needIdxs > idxs.Length) {
			int sz = Math.Max(icount + needIdxs, idxs.Length * 2);
			Array.Resize(ref idxs, sz);
			ibuffer.Dispose();
			ibuffer = device.CreateBuffer((ulong)(sz * sizeof(uint)), BufferUsage.Index | BufferUsage.CopyDst);
		}
		if (rcount + needRuns > runs.Length)
			Array.Resize(ref runs, Math.Max(rcount + needRuns, runs.Length * 2));
	}
	
	private uint addvert(in Vertex2DTextureColor v) {
		verts[vcount] = v;
		return (uint)vcount++;
	}

	private void add3idxs(uint i0, uint i1, uint i2) {
		idxs[icount++] = i0;
		idxs[icount++] = i1;
		idxs[icount++] = i2;
	}

	private void startrun(in ResolvedTextureSource tex, uint idxcount) {
		if (rcount > 0 && ReferenceEquals(runs[rcount - 1].Identity, tex.Identity))
			runs[rcount - 1].IndexCount += idxcount;
		else
			runs[rcount++] = new Run {
				Identity = tex.Identity,
				BindGroup = tex.BindGroup,
				FirstIndex = (uint)icount,
				IndexCount = idxcount
			};
	}

	public void Quad(TextureSource tex, RectF dst, RectF uv, Color32 color) {
		chk();
		ResolvedTextureSource r = tex.Resolve();
		Quad(in r, dst, uv, color);
	}

	internal void Quad(in ResolvedTextureSource tex, RectF dst, RectF uv, Color32 color) {
		chk();
		ensure(4, 6, 1);
		startrun(tex, 6);
		uint i0 = addvert(new Vertex2DTextureColor(dst.X,             dst.Y,              uv.X,            uv.Y,             color));
		uint i1 = addvert(new Vertex2DTextureColor(dst.X + dst.Width, dst.Y,              uv.X + uv.Width, uv.Y,             color));
		uint i2 = addvert(new Vertex2DTextureColor(dst.X,             dst.Y + dst.Height, uv.X,            uv.Y + uv.Height, color));
		uint i3 = addvert(new Vertex2DTextureColor(dst.X + dst.Width, dst.Y + dst.Height, uv.X + uv.Width, uv.Y + uv.Height, color));
		add3idxs(i0, i2, i1);
		add3idxs(i3, i1, i2);
	}

	public void Submit() {
		chk();
		submitted = true;

		if (icount == 0)
			return;
		ulong vbytes = (ulong)(vcount * Vertex2DTextureColor.Size);
		ulong ibytes = (ulong)(icount * sizeof(uint));
		device.WriteToBuffer(vbuffer, 0, verts.AsSpan(0, vcount));
		device.WriteToBuffer(ibuffer, 0, idxs.AsSpan(0, icount));
		pass.SetPipeline(shared.Pipeline);
		pass.SetBindGroup(0, globals.BindGroup);
		pass.SetBindGroup(1, localsUniformBindGroup);
		pass.SetVertexBuffer(0, vbuffer, 0, vbytes);
		pass.SetIndexBuffer(ibuffer, IndexFormat.Uint32, 0, ibytes);
		for (int i = 0; i < rcount; i++) {
			ref readonly Run r = ref runs[i];
			pass.SetBindGroup(2, r.BindGroup);
			pass.DrawIndexed(r.IndexCount, firstIndex: r.FirstIndex);
		}
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
