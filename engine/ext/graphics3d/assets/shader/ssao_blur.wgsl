//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Separable, depth-aware SSAO blur. AO is generated at half resolution and
//  filtered with a radius proportional to its projected world-space radius.

struct Globals {
    projection: mat4x4<f32>,
    time: f32,
}

struct SsaoBlurParams {
    inverse_projection: mat4x4<f32>,
    tuning: vec4<f32>,
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var source: texture_2d<f32>;
@group(0) @binding(2) var source_sampler: sampler;
@group(0) @binding(3) var scene_depth: texture_depth_2d;
@group(0) @binding(4) var<uniform> ssao_blur_params: SsaoBlurParams;

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
    @location(1) mode: vec4<f32>,
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = vec4<f32>(input.uv * vec2<f32>(2.0, -2.0) + vec2<f32>(-1.0, 1.0), 0.0, 1.0);
    output.uv = input.uv;
    output.mode = input.color;
    return output;
}

fn depth_at(uv: vec2<f32>) -> f32 {
    let size = textureDimensions(scene_depth);
    let max_pixel = vec2<i32>(size) - vec2<i32>(1);
    let pixel = clamp(vec2<i32>(uv * vec2<f32>(size)), vec2<i32>(0), max_pixel);
    return textureLoad(scene_depth, pixel, 0);
}

fn view_depth(uv: vec2<f32>, depth: f32) -> f32 {
    let clip = vec4<f32>(
        uv.x * 2.0 - 1.0,
        1.0 - uv.y * 2.0,
        depth,
        1.0);
    let view = ssao_blur_params.inverse_projection * clip;
    return -(view.z / max(abs(view.w), 0.000001));
}

fn gaussian_weight(offset: i32) -> f32 {
    let distance = abs(offset);
    if (distance == 1i) { return 56.0; }
    if (distance == 2i) { return 28.0; }
    if (distance == 3i) { return 8.0; }
    return 1.0;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let center = textureSample(source, source_sampler, input.uv);
    let center_depth = depth_at(input.uv);

    if (center_depth >= 0.999999) {
        return vec4<f32>(1.0);
    }

    let texture_size = vec2<f32>(textureDimensions(source));
    let center_view_depth = view_depth(input.uv, center_depth);
    let radius = ssao_blur_params.tuning.x;
    let projected_radius_pixels = min(
        ssao_blur_params.tuning.y * radius * texture_size.x / max(center_view_depth, 0.001),
        ssao_blur_params.tuning.z * radius * texture_size.y / max(center_view_depth, 0.001));
    let filter_step = clamp(projected_radius_pixels / 32.0, 1.0, 4.0);
    let texel_direction = input.mode.xy * filter_step / texture_size;

    // Nine-tap Gaussian weights [1 8 28 56 70 56 28 8 1], gated by
    // view-space depth so the wider filter cannot cross silhouettes.
    var ao_sum = center.r * 70.0;
    var weight_sum = 70.0;
    for (var i = -4i; i <= 4i; i++) {
        if (i == 0i) {
            continue;
        }

        let sample_uv = input.uv + texel_direction * f32(i);
        if (all(sample_uv > vec2<f32>(0.0)) && all(sample_uv < vec2<f32>(1.0))) {
            let sample_depth = depth_at(sample_uv);
            if (sample_depth < 0.999999) {
                let sample_value = textureSample(source, source_sampler, sample_uv);
                let delta = abs(view_depth(sample_uv, sample_depth) - center_view_depth);
                let depth_weight = 1.0 - smoothstep(radius * 0.02, radius * 0.20, delta);
                let spatial_weight = gaussian_weight(i);
                let weight = spatial_weight * depth_weight;
                ao_sum += sample_value.r * weight;
                weight_sum += weight;
            }
        }
    }

    let ao = ao_sum / max(weight_sum, 0.0001);
    return vec4<f32>(vec3<f32>(ao), 1.0);
}
