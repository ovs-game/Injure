# rendering/triangle-tutorial.md

# **This is currently like, two levels of out of date. I'll fix this soon.**

This document is, well, a tutorial on how to draw a triangle directly with `Rendering`, if you haven't guessed that yet. But the goal isn't the triangle, it's to familiarize the low-level rendering workflow and leave you off with enough knowledge to be able to do more complex rendering on your own and read through `Graphics` source code.

**This is for the low-level rendering API.** You probably don't even have to know it exists for most stuff, this is explicitly for if you want to learn how engine internals work. From game code, you'll use the high-level graphics API. For example, if you just wanna draw the exact same triangle from game code, you can do:
```csharp
OVSGame.Canvas.Triangle(new Vertex2DColor(320f, 120f, Color32.Red), new Vertex2DColor(160f, 360f, Color32.Green), new Vertex2DColor(480f, 360f, Color32.Blue));
```

This assumes you already know some graphics programming concepts and the basics of SDL2, so I won't have to go and re-explain what an index buffer or fragment shader or pipeline is. This also assumes you've already read `overview.md`.

Since you'll probably wanna follow along, make a copy the source tree, gut out everything other than `Engine/Rendering`, `Engine/SDL`, and the `.csproj` (literally, every other file can go), remove everything from the `.csproj` related to building native code / copying assets and the miniaudio package reference, and start fresh in a root-level file (or just wherever you want to).

## 0: SDL setup

I won't really explain much here. Assuming you already know how to do a basic SDL event loop, here's how you do it here:
```csharp
// all of these will be necessary later
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.SDL;
using Silk.NET.WebGPU;

using OVS.Engine.Rendering;
using OVS.Engine.SDL;

namespace Triangle;

public static unsafe class Triangle {
	// --------------------------------------------------------------------------
	public static void Main() {
		SDLOwner.InitSDL("triangle", x: 0, y: 0, w: 640, h: 480, flags: WindowFlags.Resizable);
		Init();

		Event ev;
		for (;;) {
			while (SDLOwner.SDL.PollEvent(&ev) == 1) {
				if ((EventType)ev.Type == EventType.Quit)
					goto end;
				else if ((EventType)ev.Type == EventType.Windowevent && (WindowEventID)ev.Window.Event == WindowEventID.SizeChanged)
					Resized();
			}

			Render();
			SDLOwner.SDL.Delay(16); // ~60fps
		}
end:
		Shutdown();
		SDLOwner.ShutdownSDL();
	}

	// --------------------------------------------------------------------------
	public static void Init() {
	}

	public static void Resized() {
	}

	public static void Render() {
	}

	public static void Shutdown() {
	}
}
```

This probably won't render anything if you try to run it, if you're wondering. You have to submit at least one frame for the window to be guaranteed to actually show up.

## 1: Vertex layout, renderer, and shader

Before everything else, make a blittable type to represent a vertex, something like:
```csharp
[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex2DColor(float x, float y, Color32 color) {
	public readonly float X = x;
	public readonly float Y = y;
	public readonly Color32 Color = color;

	public static readonly int Size = Unsafe.SizeOf<Vertex2DColor>();
}
```

This has a layout of:
- byte 0: `X`, taking up 4 bytes
- byte 4: `Y`, taking up 4 bytes
- byte 8: `Color32`, taking up 4 bytes

Now the first step is the shader for the pipeline. I won't go explaining WGSL's syntax, this should be understandable if you've read any shader before:
```wgsl
struct GlobalsUniform {
	proj: mat4x4<f32>
};

struct LocalsUniform {
	transform: mat4x4<f32>
};

@group(0) @binding(0)
var<uniform> globals: GlobalsUniform;

@group(1) @binding(0)
var<uniform> locals: LocalsUniform;

struct VsIn {
	@location(0) pos: vec2<f32>,
	@location(1) color: vec4<f32>
};

struct VsOut {
	@builtin(position) pos: vec4<f32>,
	@location(0) color: vec4<f32>
};

@vertex
fn vs_main(v: VsIn) -> VsOut {
	var out: VsOut;
	// 1. v.pos = normal pos in screen space
	// 2. + transform = applies transform to screen space coords
	// 3. + proj = converts screen space coords to clip coords
	out.pos = globals.proj * locals.transform * vec4<f32>(v.pos, 0.0, 1.0);
	out.color = v.color;
	return out;
}

@fragment
fn fs_main(v: VsOut) -> @location(0) vec4<f32> {
	return v.color;
}
```

You can see the globals uniform at bind group 0, and the locals uniform at bind group 1. The vertex shader applies the projection matrix to convert from pixel space to clip space, and the fragment shader just outputs the existing color. We technically don't *need* to use the locals here, since we don't really have any specific state that we can't just hardcode in, but how to manually create the GPU buffer + bind group is going to be useful for later. (Fun fact: this is the exact same shader used in `PrimitiveBatch`! You can go into `Assets/Shaders` and verify.)

The simplest solution, and what we're gonna do here, is to just store the shader source in a string and pass it to `CreateShaderWGSL()`. For that, you need to create the `WebGPURenderer`, which needs an `IRenderSurfaceSource`, which you can get from `SDLOwner`:
```csharp
public static unsafe class Triangle {
	private const string shaderSource = """
<replace this with the shader from above...>
""";

	private static WebGPURenderer renderer;
	private static GPUShader shader;

	// --------------------------------------------------------------------------
	// <Main() omitted for brevity>
	// --------------------------------------------------------------------------
	public static void Init() {
		renderer = new WebGPURenderer(SDLOwner.RenderSurfaceSource);
		shader = renderer.CreateShaderWGSL(shaderSource);
	}

	public static void Resized() {
	}

	public static void Render() {
	}

	public static void Shutdown() {
		shader.Dispose();
		renderer.Dispose();
	}
}
```

## 2: Pipeline creation

Before creating the pipeline, we need another thing, the pipeline layout. It just holds a list of bind group layouts that the pipeline expects. Here, we bind the globals at bind group 0 and the locals at 1, so:
```csharp
// earlier, next to your other `private static` fields...
private static GPUPipelineLayout pipelineLayout;

// in `Init()`, after the shader...
pipelineLayout = renderer.CreatePipelineLayout([renderer.GlobalsUniformBindGroupLayout, renderer.LocalsUniformBindGroupLayout]);

// in `Shutdown()`, at the beginning...
pipelineLayout.Dispose();
```

(Note that for the next ones, I'll only mention the bit in `Init()`, not the create a field / dispose parts.) You can see the renderer pre-defines some bind group layouts, and we just use these.

Now we can actually create the pipeline (each field is explained with a comment):
```csharp
/* private static GPURenderPipeline */ pipeline = renderer.CreateRenderPipeline(pipelineLayout, new GPURenderPipelineCreateParams(
	Shader: shader, // your shader
	VertShaderEntryPoint: "vs_main", // name of the vertex shader entry point function
	FragShaderEntryPoint: "fs_main", // name of tre fragment shader entry point function
	VertexStride: (ulong)Vertex2DColor.Size, // size of one vertex in bytes
	VertexStepMode: VertexStepMode.Vertex, // whether the GPU should advance once per vertex or instance (details aren't very relevant here)
	VertexAttributes: [
		// notice that this mirrors the layout of Vertex2DColor. if you look at your definition and compare, they both have:
		// - 2 float32s, starting at byte 0,
		// - 4 bytes read as normalized unsigned RGBA, starting at byte 8
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
	PrimitiveTopology: PrimitiveTopology.TriangleList, // how our vertex input should be interpreted
	FrontFace: FrontFace.Ccw, // how the front face is determined for culling (not very relevant here)
	CullMode: CullMode.None, // culling mode (not very relevant here)
	ColorTargetFormat: renderer.BackbufferFormat // color format of the target, here we're gonna write to the backbuffer so use that
));
```

## 5: Locals uniform and vertex/index buffers

As I said earlier, we don't really *need* the locals uniform for something this basic, and we don't need indexing either, but knowing how to do these things is useful knowledge, so you know what that means.

First the GPU buffer for the locals. At the moment, the only parameter in the locals is the transform, which should just be set to an identity matrix since we don't want an extra transform. If you want to scale/translate/etc your triangle, you can change this matrix instead of editing the vertices.  
Note that usually, you'll be using a 3x2 matrix for transform matrices since that's more convenient to work with, but the shaders specifically want 4x4 matrices, so you'll use `MatrixUtil.To4x4` to convert them. I'm demonstrating that here by converting `Matrix3x2.Identity` instead of just using `Matrix4x4.Identity`:
```csharp
/* private static GPUBuffer */ localsBuffer = renderer.CreateBuffer(
	(ulong)LocalsUniform.Size, // buffer size in bytes
	BufferUsage.Uniform | BufferUsage.CopyDst // what the buffer will be used for
);
LocalsUniform locals = new LocalsUniform {
	Transform = MatrixUtil.To4x4(Matrix3x2.Identity)
};
renderer.WriteToBuffer(localsBuffer, 0, in locals);
```

As you can see, you just create a buffer with a byte capacity and a `BufferUsage` describing what you'll use it for. `Uniform` is self-explanatory, and `CopyDst` means you can write into it, so you'll want `CopyDst` on pretty much all of your buffers. Then, you use `WriteToBuffer` to write it to there. You can use any unmanaged type / `ReadOnlySpan<T> where T : unmanaged` with `WriteToBuffer`. The second parameter is the write offset, which in this case should be zero.

Now, we need to make a bind group for that buffer:
```csharp
/* private static GPUBindGroup */ localsBindGroup = renderer.CreateBufferBindGroup(
	renderer.LocalsUniformBindGroupLayout, // bind group layout
	0, // binding slot (you can see `@binding(0)` in the shader)
	localsBuffer, // GPUBuffer
	0, // read offset
	(ulong)LocalsUniform.Size // buffer size in bytes
);
```

And... that's how you make a bind group for a buffer! Make vertex / index buffers for the triangle and do the same for them:
```csharp
// these are in pixel space (+Y = down) due to the projection from the globals applied in the shader, not clip space
Vertex2DColor[] triangleVerts = [
	new Vertex2DColor(320f, 120f, Color32.Red),
	new Vertex2DColor(160f, 360f, Color32.Green),
	new Vertex2DColor(480f, 360f, Color32.Blue)
];
uint[] triangleIndices = [0, 1, 2];

/* private static GPUBuffer */ vertexBuffer = renderer.CreateBuffer(
	(ulong)(triangleVerts.Length * Vertex2DColor.Size), // buffer size in bytes
	BufferUsage.Vertex | BufferUsage.CopyDst // what the buffer will be used for
);
/* private static GPUBuffer */ indexBuffer = renderer.CreateBuffer(
	(ulong)(triangleIndices.Length * sizeof(uint)), // buffer size in bytes
	BufferUsage.Index | BufferUsage.CopyDst // what the buffer will be used for
);

renderer.WriteToBuffer(vertexBuffer, 0, triangleVerts);
renderer.WriteToBuffer(indexBuffer, 0, triangleIndices);
```

At this point, we've made all the resources we need, and we can actually draw the triangle.

## 6: Opening a new frame+pass, drawing, and submitting

Head over to `Render()`. This code should be in there.

Since (hopefully) you've read the overview doc, you know that you have to ask the renderer for a frame:
```csharp
if (!renderer.TryBeginFrame(out RenderFrame frame))
	return;
using (frame) {
	// ...
}
```

Now, with that frame, you have to open a pass, and the pass is where you draw. You can open multiple, but only one can be open at a time. Here, we just wanna draw to the backbuffer, so open a backbuffer pass, bind everything, and draw our triangle:
```
if (!renderer.TryBeginFrame(out RenderFrame frame))
	return;
using (frame) {
	using (RenderPass pass = frame.BeginBackbufferPass(ColorAttachmentOps.Clear(Color32.Black))) {
		pass.SetPipeline(pipeline);
		pass.SetBindGroup(0, renderer.GlobalsUniformBindGroup);
		pass.SetBindGroup(1, localsBindGroup);
		pass.SetVertexBuffer(0, vertexBuffer, 0, vertexBuffer.Size);
		pass.SetIndexBuffer(indexBuffer, IndexFormat.Uint32, 0, indexBuffer.Size);
		pass.DrawIndexed(3);
	}
	frame.SubmitAndPresent();
}
```

This sets the pipeline, binds the globals at bind group 0 and the locals at bind group 1, sets the vertex and index buffers, and does an indexed draw. You can also do a non-indexed draw with plain `Draw()`. After the pass is done (and, importantly, after the pass has been disposed), `frame.SubmitAndPresent()` actually executes queued GPU work and enqueues the image for presentation.

Nothing about this is specific to only drawing one triangle, aside from the fact that the vertex/index buffers only contain... one triangle. Finally, before running this, make sure you've got everything covered in `Dispose()` and add this to `Resized`:
```csharp
public static void Resized() {
	renderer.Resized();
}
```

And you should be done! If you compile and run the program, you should see the triangle. Full code for reference:
```csharp
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.SDL;
using Silk.NET.WebGPU;

using OVS.Engine.Rendering;
using OVS.Engine.SDL;

namespace Triangle;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex2DColor(float x, float y, Color32 color) {
	public readonly float X = x;
	public readonly float Y = y;
	public readonly Color32 Color = color;

	public static readonly int Size = Unsafe.SizeOf<Vertex2DColor>();
}

public static unsafe class Triangle {
	private const string shaderSource = """
struct GlobalsUniform {
	proj: mat4x4<f32>
};

struct LocalsUniform {
	transform: mat4x4<f32>
};

@group(0) @binding(0)
var<uniform> globals: GlobalsUniform;

@group(1) @binding(0)
var<uniform> locals: LocalsUniform;

struct VsIn {
	@location(0) pos: vec2<f32>,
	@location(1) color: vec4<f32>
};

struct VsOut {
	@builtin(position) pos: vec4<f32>,
	@location(0) color: vec4<f32>
};

@vertex
fn vs_main(v: VsIn) -> VsOut {
	var out: VsOut;
	// 1. v.pos = normal pos in screen space
	// 2. + transform = applies transform to screen space coords
	// 3. + proj = converts screen space coords to clip coords
	out.pos = globals.proj * locals.transform * vec4<f32>(v.pos, 0.0, 1.0);
	out.color = v.color;
	return out;
}

@fragment
fn fs_main(v: VsOut) -> @location(0) vec4<f32> {
	return v.color;
}
""";

	private static WebGPURenderer renderer;
	private static GPUShader shader;
	private static GPUPipelineLayout pipelineLayout;
	private static GPURenderPipeline pipeline;
	private static GPUBuffer localsBuffer;
	private static GPUBindGroup localsBindGroup;
	private static GPUBuffer vertexBuffer;
	private static GPUBuffer indexBuffer;

	// --------------------------------------------------------------------------
	public static void Main() {
		SDLOwner.InitSDL("triangle", x: 0, y: 0, w: 640, h: 480, flags: WindowFlags.Resizable);
		Init();

		Event ev;
		for (;;) {
			while (SDLOwner.SDL.PollEvent(&ev) == 1) {
				if ((EventType)ev.Type == EventType.Quit)
					goto end;
				else if ((EventType)ev.Type == EventType.Windowevent && (WindowEventID)ev.Window.Event == WindowEventID.SizeChanged)
					Resized();
			}

			Render();
			SDLOwner.SDL.Delay(16); // ~60fps
		}
end:
		Shutdown();
		SDLOwner.ShutdownSDL();
	}

	// --------------------------------------------------------------------------
	public static void Init() {
		renderer = new WebGPURenderer(SDLOwner.RenderSurfaceSource);
		shader = renderer.CreateShaderWGSL(shaderSource);
		pipelineLayout = renderer.CreatePipelineLayout([renderer.GlobalsUniformBindGroupLayout, renderer.LocalsUniformBindGroupLayout]);
		pipeline = renderer.CreateRenderPipeline(pipelineLayout, new GPURenderPipelineCreateParams(
			Shader: shader, // your shader
			VertShaderEntryPoint: "vs_main", // name of the vertex shader entry point function
			FragShaderEntryPoint: "fs_main", // name of tre fragment shader entry point function
			VertexStride: (ulong)Vertex2DColor.Size, // size of one vertex in bytes
			VertexStepMode: VertexStepMode.Vertex, // whether the GPU should advance once per vertex or instance (details aren't very relevant here)
			VertexAttributes: [
				// notice that this mirrors the layout of Vertex2DColor. if you look at your definition and compare, they both have:
				// - 2 float32s, starting at byte 0,
				// - 4 bytes read as normalized unsigned RGBA, starting at byte 8
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
			PrimitiveTopology: PrimitiveTopology.TriangleList, // how our vertex input should be interpreted
			FrontFace: FrontFace.Ccw, // how the front face is determined for culling (not very relevant here)
			CullMode: CullMode.None, // culling mode (not very relevant here)
			ColorTargetFormat: renderer.BackbufferFormat // color format of the target, here we're gonna write to the backbuffer so use that
		));

		localsBuffer = renderer.CreateBuffer(
			(ulong)LocalsUniform.Size, // buffer size in bytes
			BufferUsage.Uniform | BufferUsage.CopyDst // what the buffer will be used for
		);
		LocalsUniform locals = new LocalsUniform {
			Transform = MatrixUtil.To4x4(Matrix3x2.Identity)
		};
		renderer.WriteToBuffer(localsBuffer, 0, in locals);
		localsBindGroup = renderer.CreateBufferBindGroup(
			renderer.LocalsUniformBindGroupLayout, // bind group layout
			0, // binding slot (you can see `@binding(0)` in the shader)
			localsBuffer, // GPUBuffer
			0, // read offset
			(ulong)LocalsUniform.Size // buffer size in bytes
		);

		// these are in pixel space (+Y = down) due to the projection from the globals applied in the shader, not clip space
		Vertex2DColor[] triangleVerts = [
			new Vertex2DColor(320f, 120f, Color32.Red),
			new Vertex2DColor(160f, 360f, Color32.Green),
			new Vertex2DColor(480f, 360f, Color32.Blue)
		];
		uint[] triangleIndices = [0, 1, 2];

		vertexBuffer = renderer.CreateBuffer(
			(ulong)(triangleVerts.Length * Vertex2DColor.Size), // buffer size in bytes
			BufferUsage.Vertex | BufferUsage.CopyDst // what the buffer will be used for
		);
		indexBuffer = renderer.CreateBuffer(
			(ulong)(triangleIndices.Length * sizeof(uint)), // buffer size in bytes
			BufferUsage.Index | BufferUsage.CopyDst // what the buffer will be used for
		);

		renderer.WriteToBuffer(vertexBuffer, 0, triangleVerts);
		renderer.WriteToBuffer(indexBuffer, 0, triangleIndices);
	}

	public static void Resized() {
		renderer.Resized();
	}

	public static void Render() {
		if (!renderer.TryBeginFrame(out RenderFrame frame))
			return;
		using (frame) {
			using (RenderPass pass = frame.BeginBackbufferPass(ColorAttachmentOps.Clear(Color32.Black))) {
				pass.SetPipeline(pipeline);
				pass.SetBindGroup(0, renderer.GlobalsUniformBindGroup);
				pass.SetBindGroup(1, localsBindGroup);
				pass.SetVertexBuffer(0, vertexBuffer, 0, vertexBuffer.Size);
				pass.SetIndexBuffer(indexBuffer, IndexFormat.Uint32, 0, indexBuffer.Size);
				pass.DrawIndexed(3);
			}
			frame.SubmitAndPresent();
		}
	}

	public static void Shutdown() {
		indexBuffer.Dispose();
		vertexBuffer.Dispose();
		localsBindGroup.Dispose();
		localsBuffer.Dispose();
		pipeline.Dispose();
		pipelineLayout.Dispose();
		shader.Dispose();
		renderer.Dispose();
	}
}
```

## Bonus: texturing the triangle

You'll have to create a texture + sampler first. Doing this ad hoc without create-from-image helpers is a bit of a pain, but the quickest way to get it done is to convert an image to raw RGBA using ImageMagick (`magick image.png -alpha on -depth 8 rgba:image.rgba`) and load that. You'll also have to find and note down the exact width/height of the image.

You can then create it like:
```csharp
const string imgPath = "image.rgba"; // replace this
const uint imgWidth = // ...
const uint imgHeight = // ...

byte[] rgba = File.ReadAllBytes(imgPath);
ulong expected = (ulong)imgWidth * imgHeight * 4;
if ((ulong)rgba.Length != expected)
	throw new InvalidDataException($"expected {expected} bytes, got {rgba.Length}");

/* private static GPUTexture */ texture = renderer.CreateTexture(new GPUTextureCreateParams(
	Width: imgWidth,
	Height: imgHeight,
	Format: TextureFormat.Rgba8UnormSrgb,
	Usage: TextureUsage.TextureBinding | TextureUsage.CopyDst
));

renderer.WriteToTexture(texture, new GPUTextureRegion(X: 0, Y: 0, Z: 0, Width: imgWidth, Height: imgHeight),
	rgba, new GPUTextureLayout(Offset: 0, BytesPerRow: imgWidth * 4, RowsPerImage: imgHeight));
/* private static GPUSampler */ sampler = renderer.CreateSampler(SamplerStates.NearestClamp);

/* private static GPUBindGroup */ texBindGroup = renderer.CreateTextureBindGroup(texture, sampler);
```

As you can see, the process is pretty similar to creating a buffer - you make a texture, then write to it separately, and use a helper in `WebGPURenderer` to make a bind group.

To do textured drawing, bind a texture + sampler at bind group 2 (or anywhere, but bind group 2 is the standard convention as you've read in the overview doc) and sample it from your fragment shader. You'll also want UV info in your vertices.

Switch over your code to some new vertex type that stores UV, like:
```csharp
[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex2DTextureColor(float x, float y, float u, float v, Color32 color) {
	public readonly float X = x;
	public readonly float Y = y;
	public readonly float U = u;
	public readonly float V = v;
	public readonly Color32 Color = color;

	public static readonly int Size = Unsafe.SizeOf<Vertex2DTextureColor>();
}
```

In your shader, add UV info to the vertex types too:
```wgsl
struct VsIn {
	@location(0) pos: vec2<f32>,
	@location(1) uv: vec2<f32>,
	@location(2) color: vec4<f32>
};

struct VsOut {
	@builtin(position) pos: vec4<f32>,
	@location(0) uv: vec2<f32>,
	@location(1) color: vec4<f32>
};
```

Add a texture + sampler at bind group 2:
```wgsl
@group(2) @binding(0)
var tex: texture_2d<f32>;
@group(2) @binding(1)
var smp: sampler;
```

change the fragment shader to sample the texture:
```wgsl
@fragment
fn fs_main(v: VsOut) -> @location(0) vec4<f32> {
	return textureSample(tex, smp, v.uv) * v.color;
}
```

and add `out.uv = v.uv;` into the vertex shader. (That `* v.color` after the sample will multiplicative tint the texture with the color of the colored geometry. I'd recommend keeping that in and just making the geometry white if you don't want a tint, since this is the standard convention used in the `TexturedBatch` shader.)

Map your triangle's verts to UV coords:
```csharp
Vertex2DTextureColor[] triangleVerts = [
	new Vertex2DTextureColor(320f, 120f, 0.5f, 0f, Color32.Red),
	new Vertex2DTextureColor(160f, 360f, 0f,   1f, Color32.Green),
	new Vertex2DTextureColor(480f, 360f, 1f,   1f, Color32.Blue)
];
```

Now your pipeline layout and vertex definition inside the pipeline creation should be:
```csharp
pipelineLayout = renderer.CreatePipelineLayout([
	renderer.GlobalsUniformBindGroupLayout,
	renderer.LocalsUniformBindGroupLayout,
	renderer.TextureBindGroupLayout
]);

	// inside CreateRenderPipeline ...
	VertexAttributes: [
		// notice that this mirrors the layout of Vertex2DTextureColor. if you look at your definition and compare, they both have:
		// - 2 float32s, starting at byte 0,
		// - 2 float32s, starting at byte 8,
		// - 4 bytes read as normalized unsigned RGBA, starting at byte 16
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
```

After this, all you have to do is bind the texture + sampler bind group in your pass with `pass.SetBindGroup(2, texBindGroup);`, and you should see the texture on your triangle! (Don't forget to correctly dispose everything in `Shutdown()`, it's good practice.)

## Further reading

The most useful files to read through next are `PrimitiveBatch` and `TexturedBatch`. They operate internally on very similar principles to here, you should recognize a lot of stuff. Have fun!
