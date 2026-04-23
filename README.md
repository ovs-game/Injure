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
miscellaneous TODO (see issues for the big ones):

- add a test game into the source tree so testing is less of a pain in the ass
- public optimized swizzle/convert api
  - more SIMD kernels, there's barely any right now
  - benchmarks, as well as looking at the codegen to make sure nothing stupid is going on
  - maybe a few more packed formats, also maybe arbitrary RGBA8x4 / mayybe RGB8x3 permutations
- add an uncaptured error callback and an error scope api into `Rendering`, right now something like a validation error triggers a panic in `wgpu-native` and then it can't unwind across an ffi boundary so it just aborts the entire process
- fix paragraphs in doc comments (i found out it doesn't work like markdown and you need explicit `<para>`)
- asset file watchers (FOR REAL THIS TIME (
- windows builds
- streaming textures and readback
- proper depth/stencil support incl. drawing to them and sampling them through Canvas
- `.tex.json` should be the primary way to load textures, add more stuff there
- figure out how we're gonna handle `AssetRef`s for sounds since unlike `Texture2D` they carry MA state
- a good asset replace/hotpatch/hook system, you can do it right now but it's a bit clunky
- conveniences for mask drawing and animations, probably IAnimatedTexture with texture list / animated webp implementations
