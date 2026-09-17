//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Post-process: 13-tap downsample (Jimenez 2014)
//  Optional brightness threshold on first pass via vertex color.r

struct Globals {
    projection: mat4x4<f32>,
    time: f32,
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var source: texture_2d<f32>;
@group(0) @binding(2) var source_sampler: sampler;
@group(0) @binding(3) var bone_texture: texture_2d<f32>;

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

// Threshold each tap before averaging: a small bright insert must not disappear
// just because it shares a downsample footprint with a dark lantern frame.
fn bloom_sample(uv: vec2<f32>, threshold: f32) -> vec4<f32> {
    let sample_color = textureSample(source, source_sampler, uv);
    if (threshold <= 0.0) { return sample_color; }
    let color = max(sample_color.rgb, vec3<f32>(0));
    // Peak channel treats blue/rose emitters like warm ones. Luminance-only
    // extraction misses saturated blue even when its blue channel is HDR.
    let brightness = max(color.r, max(color.g, color.b));
    let knee = max(threshold * .25, .0001);
    let soft = clamp(brightness - threshold + knee, 0.0, 2.0 * knee);
    let excess = max(brightness - threshold, soft * soft / (4.0 * knee));
    return vec4<f32>(color * (excess / max(brightness, .0001)), sample_color.a);
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    // color.r = threshold (0 = no threshold), color.g = texel_w, color.b = texel_h
    let t = vec2(input.color.g, input.color.b);
    let uv = input.uv;

    // 13-tap downsample: 4 corner quads + 4 edge quads + 1 center
    // Each bilinear sample covers a 2x2 texel region
    let a = bloom_sample(uv + vec2(-1.0, -1.0) * t, input.color.r);
    let b = bloom_sample(uv + vec2( 1.0, -1.0) * t, input.color.r);
    let c = bloom_sample(uv + vec2(-1.0,  1.0) * t, input.color.r);
    let d = bloom_sample(uv + vec2( 1.0,  1.0) * t, input.color.r);

    let e = bloom_sample(uv + vec2(-2.0, -2.0) * t, input.color.r);
    let f = bloom_sample(uv + vec2( 0.0, -2.0) * t, input.color.r);
    let g = bloom_sample(uv + vec2( 2.0, -2.0) * t, input.color.r);

    let h = bloom_sample(uv + vec2(-2.0,  0.0) * t, input.color.r);
    let i = bloom_sample(uv, input.color.r);
    let j = bloom_sample(uv + vec2( 2.0,  0.0) * t, input.color.r);

    let k = bloom_sample(uv + vec2(-2.0,  2.0) * t, input.color.r);
    let l = bloom_sample(uv + vec2( 0.0,  2.0) * t, input.color.r);
    let m = bloom_sample(uv + vec2( 2.0,  2.0) * t, input.color.r);

    // Weighted combination: center-heavy to prevent fireflies
    // Inner 4 quads (a,b,c,d) get 0.5 total, outer 9 (e-m) get 0.5 total
    var color = (a + b + c + d) * 0.125;                          // 4 * 0.125 = 0.5
    color += (e + g + k + m) * 0.03125;                           // 4 * 0.03125 = 0.125
    color += (f + h + j + l) * 0.0625;                            // 4 * 0.0625 = 0.25
    color += i * 0.125;                                            // 0.125
    // Total = 0.5 + 0.125 + 0.25 + 0.125 = 1.0

    return color;
}
