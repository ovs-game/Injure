## completely broken as of right now while i sort out a bunch of internal stuff

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

- [active] custom closed enum types, aiming for `[ClosedEnum]` / `[ClosedFlags]` / `[ClosedUnion]`
- switch over the codebase to the closed enum types
- finish the input system, it got put on hold because c# enums suck
- dedicated docs for the asset system. honestly, most of the engine needs dedicated docs, but assets should probably be first, it doesn't work like a traditional asset system
- add a test game into the source tree so testing is less of a pain in the ass
- public optimized swizzle/convert api
  - more SIMD kernels, there's barely any right now
  - benchmarks, as well as looking at the codegen to make sure nothing stupid is going on
  - maybe a few more packed formats, also maybe arbitrary RGBA8x4 / mayybe RGB8x3 permutations
- ~~FIX~~ WRITE THE RENDERING DOCS!!! i don't have the image i wanted to attach alongside this
- add an uncaptured error callback and an error scope api into `Rendering`, right now something like a validation error triggers a panic in `wgpu-native` and then it can't unwind across an ffi boundary so it just aborts the entire process
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
