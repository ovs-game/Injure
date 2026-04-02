# rendering/lifetimes.md

This document describes the ownership and lifetime semantics of the `Rendering` API.

The API is small, but many of its failure modes are lifetime failures rather than logic / correctness failures:
- disposing a resource when it'd still be needed later down the line for a submit
- not closing the pass before submitting the frame
- wrapping a borrowed handle like an owning object

This file is around these kinds of contracts.

## Lifetime categories

Broadly speaking, the rendering layer has three kinds of GPU/rendering-related objects:
- owning objects
- scoped objects
- borrowed handle/reference objects

This distinction should be kept in mind when adding new types.

All GPU/rendering-related objects are created from a `WebGPURenderer` and are bound to its underlying WebGPU device/state. This means that:
- once the `WebGPURenderer` is disposed, all objects created from it become invalid. This includes both low-level wrappers and high-level drawing APIs in `Graphics`, since everything is built on top.
- since wrappers and such may pass around their references to a `WebGPURenderer` to other newly created objects, which then may do the same, etc etc., having multiple `WebGPURenderer`s at once would require extensive, complicated dependency graph tracking.

So the intended usage model is that there is only one `WebGPURenderer` at a time globally, and once it's disposed **all** rendering-related objects become invalid. The public get-only properties for bind groups / bind group layouts / current state throw `ObjectDisposedException` on use-after-dispose, and any copied references to them also become invalid.

## Owning objects

These own raw WebGPU resources and are responsible for releasing them on `Dispose()`, and are typically more long-lived.

Examples:
- `WebGPURenderer`
- `GPUBuffer`
- `GPUTexture`
- `GPURenderPipeline`
- `GPUBindGroup` (but NOT `GPUBindGroupRef`)
- `GPUBindGroupLayout` (but NOT `GPUBindGroupLayoutRef`)

Unless explicitly documented otherwise, disposing one will immediately release the underlying WebGPU resources and either null any public fields / properties / other accessors to them, invalidating further use.

## Scoped objects

These are only valid for some narrow execution scope and must not be cached like a normal resource. These are typically intended to be used with a `using` scope.

Some examples:

### `RenderFrame`

A `RenderFrame` object is valid only for one frame's encoding/submission lifetime. The exact semantics are:
- a frame cannot have more than one open `RenderPass` at a time, so "is there an active pass" is a binary question, not an "active pass count"
- submitting a frame makes it unusable and also disposes it, making `Dispose()` a no-op. `Dispose()` without submitting discards the frame. You should always call `Dispose()` either way for correctness.
- submitting **or disposing** while a pass is still active is a bug and as such always throws `InvalidOperationException`. This does mean that `Dispose()` can throw, and this decision is intentional, since if you're disposing a frame with an active pass, that's always a bug in your code, not something to be cleaned up.

### `RenderPass`

A `RenderPass` object is valid only while its owning frame is valid and its underlying WebGPU pass encoder is open. The exact semantics are:
- it must NOT outlive the `RenderFrame` it came from. Once that frame is invalid, so is the pass.
- a pass is finalized by disposing it, there is no way to discard it. This means that, just like `RenderFrame`, submit is final and irreversible.

## Borrowed handle/reference objects

Some wrapper types distinguish owning vs borrowing, as described in the overview doc.  
Currently, the wrapper types that distinguish owned/borrowed are:
- `GPUBindGroup` (`GPUBindGroupHandle` / `GPUBindGroup` / `GPUBindGroupRef`)
- `GPUBindGroupLayout` (`GPUBindGroupLayoutHandle` / `GPUBindGroupLayout` / `GPUBindGroupLayoutRef`)
 
The borrowed variants exist because some bind groups / layouts are renderer-owned globals that others need to be able to use for binding / pipeline creation but be unable to dispose. This distinction is part of the ownership model, not just an implementation detail, and more existing types may start to distinguish owned/borrowed in the future.

A ref wrapper type can be created in two different ways:
- from an underlying owning object (for example, a `GPUBindGroupRef` created from an underlying `GPUBindGroup`)
- from a raw underlying pointer to a WebGPU resource (for example, a `GPUBindGroupRef` created from a raw `BindGroup *`)

These two cases have different lifetime behavior.

### Source-backed refs

If a ref is created from an owning object, the ref remains tied to that owner's current internal pointer(s). This means that:
- when the owner is disposed and as such its stored pointers get nulled, the ref also becomes invalid
- calling `DangerousGetPtr()` on the ref then returns null

### Pointer-backed refs

If a ref is created directly from a pointer to a WebGPU resource, such lifetime tracking isn't possible. It just does a dumb "store the given pointer", as it has no way to know where that pointer came from or when the owner goes away. As such:
- once the underlying resource is released, the ref becomes a dangling pointer
- its validity must instead be enforced by whatever's exposing it
- in practice, this usually means a public property/method that returns null or throws post-dispose

You can see this in practice in `WebGPURenderer`. The bind groups and bind group layouts exposed via public properties are pointer-backed, and the validity is enforced by the containing API: once the `WebGPURenderer` is disposed, those properties throw `ObjectDisposedException` instead of returning the backing field.  
This is a general pattern for pointer-backed refs; since it can't do it itself, lifetime validation must happen in the API that exposes it.

Source-backed refs are stronger and should typically be preferred, while pointer-backed refs are for when there's only a raw WebGPU pointer and the containing object/API can stop exposing the ref once it's invalid, since in that case it saves the churn of creating an owning wrapper.

## Deferred disposal

Some GPU resources may become dead to CPU-side code before they're safe to actually destroy, because already-encoded GPU work is still referencing them and will need them later, e.g for a submit. The solution to this is to defer the disposal until some later, well-defined point.

The relevant boundary here is frame submission; resources must survive until the frame is submitted so that they can actually be used in the resulting frame, and after the submit the WebGPU command buffer no longer needs the resources. As such, the only deferred disposal API currently exposed is `RenderFrame.DisposeAfterSubmit(...)`.

Typical usecases are short-lived per-frame / per-batch objects. For example, in a higher-level draw wrapper that binds to a pass, you may want to create a GPU buffer + bind group for the locals uniform. However, once the frame is actually submitted, this will cause an error as it'll run into a dangling reference to a resource when the encoded work is being executed.  
The solution is to pass the buffer + bind group to `frame.DisposeAfterSubmit()`, assuming you have a reference to the frame. The buffer and bind group will be kept alive until the submit, after which they're no longer needed and will be disposed.

You can sign up any `IDisposable` like this. If the frame is discarded (plain `Dispose()`, no submit), they'll still be disposed. The registered `IDisposable`s are disposed in an indeterminate order.

## Guidance for future types

When adding a new GPU/rendering-related type, think about:
- whether it owns any WebGPU resources or only needs references to them
- whether it can/should be cached or if it should be used like a scope with `using`
- whether it needs both owned and borrowed forms, or doesn't need one of them for now (the recommendation is to only split them if you run into a real case where having split owned/borrowed would make an API better, otherwise it just pollutes the namespace and creates boilerplate)
- whether it's intended to outlive a pass/frame/etc or end within one, and whether it holds anything that may need to live longer

You're gonna have to figure that out at some point, and not doing it upfront will usually just mean redesign pain later.
