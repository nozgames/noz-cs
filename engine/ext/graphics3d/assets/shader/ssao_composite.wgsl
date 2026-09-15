//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Full-resolution SSAO composite. The filtered AO texture is upsampled over
//  the untouched scene color; vertex color.r selects the diagnostic view.

struct Globals {
    projection: mat4x4<f32>,
    time: f32,
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var ao_texture: texture_2d<f32>;
@group(0) @binding(2) var ao_sampler: sampler;
@group(0) @binding(3) var scene_texture: texture_2d<f32>;
@group(0) @binding(4) var scene_sampler: sampler;

struct VertexInput {
    @location(0) position: vec2<f32>,
    @location(1) uv: vec2<f32>,
    @location(2) normal: vec2<f32>,
    @location(3) color: vec4<f32>,
    @location(4) bone: i32,
    @location(5) atlas: i32,
    @location(6) frame_count: i32,
    @location(7) frame_width: f32,
    @location(8) frame_rate: f32,
    @location(9) frame_time: f32,
    @location(10) overlay_color: vec4<f32>,
}

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) debug_view: f32,
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = vec4<f32>(input.uv * vec2<f32>(2.0, -2.0) + vec2<f32>(-1.0, 1.0), 0.0, 1.0);
    output.uv = input.uv;
    output.debug_view = input.color.r;
    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let ao = textureSample(ao_texture, ao_sampler, input.uv).r;
    if (input.debug_view > 0.5) {
        return vec4<f32>(vec3<f32>(ao), 1.0);
    }

    let scene = textureSample(scene_texture, scene_sampler, input.uv);
    return vec4<f32>(scene.rgb * ao, scene.a);
}
