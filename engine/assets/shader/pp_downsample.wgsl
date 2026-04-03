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

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    // color.r = threshold (0 = no threshold), color.g = texel_w, color.b = texel_h
    let t = vec2(input.color.g, input.color.b);
    let uv = input.uv;

    // 13-tap downsample: 4 corner quads + 4 edge quads + 1 center
    // Each bilinear sample covers a 2x2 texel region
    let a = textureSample(source, source_sampler, uv + vec2(-1.0, -1.0) * t);
    let b = textureSample(source, source_sampler, uv + vec2( 1.0, -1.0) * t);
    let c = textureSample(source, source_sampler, uv + vec2(-1.0,  1.0) * t);
    let d = textureSample(source, source_sampler, uv + vec2( 1.0,  1.0) * t);

    let e = textureSample(source, source_sampler, uv + vec2(-2.0, -2.0) * t);
    let f = textureSample(source, source_sampler, uv + vec2( 0.0, -2.0) * t);
    let g = textureSample(source, source_sampler, uv + vec2( 2.0, -2.0) * t);

    let h = textureSample(source, source_sampler, uv + vec2(-2.0,  0.0) * t);
    let i = textureSample(source, source_sampler, uv);
    let j = textureSample(source, source_sampler, uv + vec2( 2.0,  0.0) * t);

    let k = textureSample(source, source_sampler, uv + vec2(-2.0,  2.0) * t);
    let l = textureSample(source, source_sampler, uv + vec2( 0.0,  2.0) * t);
    let m = textureSample(source, source_sampler, uv + vec2( 2.0,  2.0) * t);

    // Weighted combination: center-heavy to prevent fireflies
    // Inner 4 quads (a,b,c,d) get 0.5 total, outer 9 (e-m) get 0.5 total
    var color = (a + b + c + d) * 0.125;                          // 4 * 0.125 = 0.5
    color += (e + g + k + m) * 0.03125;                           // 4 * 0.03125 = 0.125
    color += (f + h + j + l) * 0.0625;                            // 4 * 0.0625 = 0.25
    color += i * 0.125;                                            // 0.125
    // Total = 0.5 + 0.125 + 0.25 + 0.125 = 1.0

    // Optional threshold (color.r > 0 means apply threshold)
    let threshold = input.color.r;
    if (threshold > 0.0) {
        let luminance = dot(color.rgb, vec3(0.2126, 0.7152, 0.0722));
        let knee = 0.01;
        let soft = luminance - threshold + knee;
        let contribution = clamp(soft / (2.0 * knee + 0.0001), 0.0, 1.0);
        color = vec4(color.rgb * contribution, color.a);
    }

    return color;
}
