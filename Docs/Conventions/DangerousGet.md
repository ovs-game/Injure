# Conventions/DangerousGet.md

Some types expose methods starting with `DangerousGet`, for example `Graphics.Texture2D.DangerousGetBindGroup()`. Such methods return some underlying value/resource that basically lets you bypass some ownership/lifetime/revocation model or API. Usually, this means either:
- Exposing a native handle / pointer / something else that can't carry ownership data. Example: most of the `GPU*` types in `Rendering` are just wrappers over a WebGPU object + some metadata. Once an owning object gets disposed, all the non-owning views created from it update accordingly, but if you grab the pointer to the underlying WebGPU object, once the owner is disposed you're just left with a dangling pointer. Additionally, there's nothing stopping you from just calling into WebGPU to release that pointer, even if you got it from a non-owning view, which weakens the model.
- Exposing an object that can outlive the intended lifetime. Example: implementors of `IRevokable` from the asset system are expected to make the object logically unusable once `Revoke()` is called and fail fast on usage attempts, and since revocation is just logical invalidation, if there's something you can cache to keep using the object, that defeats the point. Take `Graphics.Texture2D` as an example: you could just cache the `GPUTexture` + `GPUSampler` or the bind group and keep using the texture just fine post-revoke.
though that's not all it's limited to, it's "anything that can bypass/weaken the intended ownership/lifetime/revocation model".

Reusing these earlier examples, being able to actually access the data behind a GPU object or a texture is a pretty big deal, and blocking that off in the name of safety is just gonna incentivize people to fish out the value out of a private field using reflection and not actually buy any safety. So, instead, they're exposed publicly, but under a name that bluntly communicates "you should know what you're doing". "Dangerous" doesn't inherently mean "memory-unsafe" or something like that.

Additionally, since some of these methods expose native handles and such, their return types or values may change completely if, for example, the codebase switches from one library to another or if internals shuffle around; these usually have a note in their `<remarks>` that reads something like:
```csharp
/// <remarks>
/// <b>The return type is not a stable API and may change without notice.</b>
/// See <c>Docs/Conventions/DangerousGet.md</c> on <c>DangerousGet*</c> methods for more info.
/// </remarks>
```
