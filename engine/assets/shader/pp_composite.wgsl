//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Post-process: bloom composite (additive blend of bloom onto original)

struct Globals {
    projection: mat4x4<f32>,
    time: f32,
}

struct CompositeParams {
    intensity: f32,
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var bloom_texture: texture_2d<f32>;
@group(0) @binding(2) var bloom_sampler: sampler;
@group(0) @binding(3) var bone_texture: texture_2d<f32>;
@group(0) @binding(4) var<uniform> composite_params: CompositeParams;
@group(0) @binding(5) var original_texture: texture_2d<f32>;
@group(0) @binding(6) var original_sampler: sampler;

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
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = globals.projection * vec4<f32>(input.position, 0.0, 1.0);
    output.uv = input.uv;
    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let original = textureSample(original_texture, original_sampler, input.uv);
    let bloom = textureSample(bloom_texture, bloom_sampler, input.uv);
    return vec4(original.rgb + bloom.rgb * composite_params.intensity, original.a);
}
