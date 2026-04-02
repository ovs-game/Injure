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

@group(2) @binding(0)
var tex: texture_2d<f32>;
@group(2) @binding(1)
var smp: sampler;

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

@vertex
fn vs_main(v: VsIn) -> VsOut {
	var out: VsOut;
	// 1. v.pos = normal pos in screen space
	// 2. + transform = applies transform to screen space coords
	// 3. + proj = converts screen space coords to clip coords
	out.pos = globals.proj * locals.transform * vec4<f32>(v.pos, 0.0, 1.0);
	out.uv = v.uv;
	out.color = v.color;
	return out;
}

@fragment
fn fs_main(v: VsOut) -> @location(0) vec4<f32> {
	return textureSample(tex, smp, v.uv) * v.color;
}
