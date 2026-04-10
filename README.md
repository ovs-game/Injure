game framework thing that was born because XNA/FNA kind of sucked, currently in early active development and not very usable yet, built on top of SDL2, WebGPU, and miniaudio

----------

before you can build the project, build the native deps with:
```sh
git submodule update --init --recursive # if you haven't already cloned them yet
make -C Injure.Native/Native RID=linux-x64 # replace linux-arm64 with one of: osx-x64, osx-arm64, linux-x64, linux-arm64
```
you only need to do this once, unless you wanna update/rebuild them. yes, only macos and linux are supported right now, sorry

----------
TODO:

- [this one is next-up] test if it actually Works since the latest commit
- [this one is next-up] finish the Big fat rendering stack refactor
- [this one is next-up] public optimized swizzle/convert api
- [this one is next-up] high-level texture creation / upload api
- [next-up afterwards] FIX THE RENDERING DOCS!!! i don't have the image i wanted to attach alongside this
- [next-up afterwards #2] layers system
- [next-up afterwards #2] clocks, clock filtering, etc.
- [really priority] dedicated docs for the asset system. honestly, most of the engine needs dedicated docs, but assets should probably be first, it doesn't work like a traditional asset system
- [Idk when but this should be done] stop having the todo here and use github issues
- the text renderer
  - [priority] write tests for like everything that can be automatically tested, this is also going to involve somehow being able to do things that need `WebGPURenderer` without actually pulling a renderer
  - way to scale/etc text post-make
  - more ways to tweak the layout
    - alignment, at least basic left/center/right
    - justify
    - consistent vertical metrics across fallbacks
    - adjustable trim/ws handling at line boundaries
    - max length / max lines / truncation with ellipsis
    - overflow mode
    - line/para spacing
  - some optimizations
    - less allocation spam
    - cache break iterators keyed by locale
    - per-glyph atlas eviction, this sounds simple but it's probably gonna need a small custom allocator
    - better caching in general
    - maybe untangle some of the indirection spaghetti
  - fancier features
    - color fonts / BGRA
    - knuth-plass linebreaking
    - SDF/MSDF rasterization
    - text effects, outlines, etc.
    - per-glyph effects
- fix paragraphs in doc comments (i found out it doesn't work like markdown and you need explicit `<para>`)
- asset file watchers (FOR REAL THIS TIME (
- windows builds
- more tests, like a Lot more
- streaming textures and readback
- proper depth/stencil support incl. drawing to them and sampling them through Canvas
- replace `InvalidOperationException`s in `Core/` and `SDL/` with a proper `SDLException` and the ones in `Timing/` with uhh idk something
- `.tex.json` should be the primary way to load textures, add more stuff there
- think about whether we need a roslyn analyzer
- figure out how we're gonna handle `AssetRef`s for sounds since unlike `Texture2D` they carry MA state
- mouse input api, it doesn't really need to be hi res since it's just for ui stuff
- real InputSystem remapping based on a json keymap / button map
- controller registry and controller input dispatching/handling
- a good asset replace/hotpatch/hook system, you can do it right now but it's a bit clunky
- mipmap-like system for textures with a "known viewport resolutions" registry
- conveniences for mask drawing and animations, probably IAnimatedTexture with texture list / animated webp implementations
- think about whether Injure.Native and/or Injure should become local nuget packages, probably not since a local nuget feed is probably more churn than necessary and the only benefit is not having to do `-r <RID> -p:InjureNativeRID=<RID>`
