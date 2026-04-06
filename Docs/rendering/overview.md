# rendering/overview.md

# **This is currently really of out of date. I'll fix this soon.**

The drawing layer provides a 2D-oriented abstraction over WebGPU, and is split into two namespaces:
- `Graphics` - higher-level abstractions such as `Canvas`, draw batches, `Texture2D`, `RenderTarget2D`, etc.
- `Rendering` - low-level rendering API wrapping over WebGPU, resources, pass/frame, etc.

This document covers the low-level rendering API in `Rendering`.

## `WebGPURenderer`

is the main abstraction over WebGPU. It:
- owns the WebGPU instance, surface, adapter, device, queue, and surface config
- owns global bind groups / bind group layouts and exposes public properties to them for binding / pipeline layout creation
- is the place for GPU resource creation using public helper methods
- exposes the lowest-level rendering API with `TryBeginFrame`, which tries to get the surface's texture, creates a command encoder, and returns a `RenderFrame` for them.
This is the rendering "core". Most other rendering objects build on top and all become invalid once the renderer is disposed.

## `RenderFrame`

represents a single render frame, and allows for opening passes to the backbuffer / a `GPURenderTarget` and then submitting the frame. After a submit, the frame becomes unusable.

It owns the command encoder used for all the passes in that frame, and has a deferred disposal registry interface for resources that must survive for the frame submit (e.g bind groups / GPU buffers). Only one render pass may be open at a time.

## `RenderPass`

is a wrapper over a WebGPU pass encoder, returned by `RenderFrame`'s pass-opening methods, and intended to be used as a scope with `using`, not as a free-floating object. It exposes methods for pass-local operations:
```csharp
public void SetPipeline(GPURenderPipeline pipeline);
public void SetBindGroup(uint index, GPUBindGroupHandle bindGroup);
public void SetVertexBuffer(uint slot, GPUBuffer buffer, ulong offset = 0, ulong size = WholeSize);
public void SetIndexBuffer(GPUBuffer buffer, IndexFormat format, ulong offset = 0, ulong size = WholeSize);
public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0);
public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0);
```

## Wrapper types

Commonly useful WebGPU objects are wrapped in small C# types instead of being exposed and passed around as raw pointers. The reasons are to:
- centralize ownership / disposal
- keep APIs usable from non-`unsafe` code
- allow higher-level code to deal with GPU resources without knowing about raw WebGPU types

Some of these types distinguish owning vs borrowing. For example, `GPUBindGroup` has:
- `GPUBindGroup` (owns the underlying bind group, has a `Dispose()` method)
- `GPUBindGroupRef` (does not own the underlying bind group, has no `Dispose()`)
which both inherit from an abstract class `GPUBindGroupHandle`. Public APIs that deal with these types that distinguish owned/borrowed should try to take the handle type wherever possible.

Wrapper types typically expose a `DangerousGetPtr()` method (or more specific equivalents for multi-pointer ones) so that the underlying WebGPU object is still accessible and usable for `unsafe` code.

## Bindings

### Globals

The globals bind group, typically bound at bind group 0, is a uniform containing data shared for the entire renderer or frame. At the moment, it contains:
```csharp
Matrix4x4 Projection; // WGSL: `proj: mat4x4<f32>`
```

This bind group is owned by the `WebGPURenderer`, exposed by the properties:
```csharp
public GPUBindGroupLayoutRef GlobalsUniformBindGroupLayout { get; }
public GPUBindGroupRef GlobalsUniformBindGroup { get; }
```

### Texture + sampler

Textured drawing uses another bind group. The `WebGPURenderer` exposes a method `CreateTextureBindGroup` to create this bind group, which is intended to be created by users (batches, etc.) and bound. The layout is exposed by the `WebGPURenderer` as:
```csharp
public GPUBindGroupLayoutRef TextureBindGroupLayout { get; }
```

The wrapper for an offscreen render target, `GPURenderTarget`, is accepted by `CreateTextureBindGroup` via an overload.

## Pipeline creation

A `GPUPipelineLayout` is created using a list of bind group layouts (impl. note: `ReadOnlySpan<GPUBindGroupLayoutHandle>`) and is used in the creation of a render pipeline. A typical pipeline is created from a:
- pipeline layout
- shader
- vertex layout declaration
- primitive state
- target format
- optional blend state

Note that pipelines are target format specific. This is relevant when rendering to offscreen render targets or generally formats other than the backbuffer.

## Basic types

The `Rendering` namespace also contains some basic value types for convenience and consistency, such as:
- `Color32`, representing a blittable 32bpp RGBA color
- `ColorAttachmentOps`, describing how a color attachment should be treated on pass begin/end, e.g determining whether previous contents are preserved or discarded when a target is reopened in a new pass.
- `GlobalsUniform` / `LocalsUniform` for the uniforms described above

These types are directly used in APIs, at ABI boundaries, etc. and not just conveniences that happen to live in `Rendering`. There is also `MatrixUtil`, a static class containing utilities for matrix creation / conversion.

## Typical usage flow

Simplified, the typical flow is something like:
- ask `WebGPURenderer` for a `RenderFrame`
- open a `RenderPass` to the backbuffer or a render target
- bind pipeline, bind groups, buffers
- do draw calls
- end pass
- submit frame

Pseudocode:
```csharp
if (!renderer.TryBeginFrame(out RenderFrame frame))
	return; // skip this frame
using (frame) {
	// draw to the backbuffer, clear it with black
	using (RenderPass pass = frame.BeginBackbufferPass(ColorAttachmentOps.Clear(Color32.Black))) {
		pass.SetPipeline(...);
		pass.SetBindGroup(0, ...);
		pass.SetVertexBuffer(0, ...);
		pass.SetIndexBuffer(0, ...);
		pass.DrawIndexed(...);
	}
	frame.Submit();
}
```

Real code will typically layer abstractions over this, but this is the core rendering API. (Note that `TryBeginFrame` returning `false` is not fatal and typically means you should just skip the frame. Truly fatal failures throw exceptions.)

## See also

`lifetimes.md`, for how resource ownership / lifetime tracking is handled in the renderer layer.
`triangle-tutorial.md`, for a more comprehensive real code example.
