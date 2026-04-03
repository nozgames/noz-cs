//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Post-process: 9-tap tent upsample + blend with mip at target resolution

struct Globals {
    projection: mat4x4<f32>,
    time: f32,
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var source: texture_2d<f32>;
@group(0) @binding(2) var source_sampler: sampler;
@group(0) @binding(3) var bone_texture: texture_2d<f32>;
@group(0) @binding(5) var mip_texture: texture_2d<f32>;
@group(0) @binding(6) var mip_sampler: sampler;

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
    @location(1) color: vec4<f32>,
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = globals.projection * vec4<f32>(input.position, 0.0, 1.0);
    output.uv = input.uv;
    output.color = input.color;
    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    // color.g = texel_w, color.b = texel_h
    let t = vec2(input.color.g, input.color.b);
    let uv = input.uv;

    // 9-tap tent filter on the smaller (source) mip
    var result = textureSample(source, source_sampler, uv + vec2(-1.0, -1.0) * t) * 1.0;
    result +=    textureSample(source, source_sampler, uv + vec2( 0.0, -1.0) * t) * 2.0;
    result +=    textureSample(source, source_sampler, uv + vec2( 1.0, -1.0) * t) * 1.0;
    result +=    textureSample(source, source_sampler, uv + vec2(-1.0,  0.0) * t) * 2.0;
    result +=    textureSample(source, source_sampler, uv)                         * 4.0;
    result +=    textureSample(source, source_sampler, uv + vec2( 1.0,  0.0) * t) * 2.0;
    result +=    textureSample(source, source_sampler, uv + vec2(-1.0,  1.0) * t) * 1.0;
    result +=    textureSample(source, source_sampler, uv + vec2( 0.0,  1.0) * t) * 2.0;
    result +=    textureSample(source, source_sampler, uv + vec2( 1.0,  1.0) * t) * 1.0;
    result /= 16.0;

    // Blend with the downsampled mip at the target resolution
    let mip = textureSample(mip_texture, mip_sampler, uv);
    return result + mip;
}
