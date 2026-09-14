//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Depth-only, stylized SSAO. View-space normals are reconstructed from
//  neighboring depth samples so the scene does not need a normal target.

struct Globals {
    projection: mat4x4<f32>,
    time: f32,
}

struct SsaoParams {
    inverse_projection: mat4x4<f32>,
    projection_scale: vec4<f32>,
    tuning: vec4<f32>,
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var source: texture_2d<f32>;
@group(0) @binding(2) var source_sampler: sampler;
@group(0) @binding(3) var scene_depth: texture_depth_2d;
@group(0) @binding(4) var<uniform> ssao_params: SsaoParams;

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

fn depth_at(uv: vec2<f32>) -> f32 {
    let size = textureDimensions(scene_depth);
    let max_pixel = vec2<i32>(size) - vec2<i32>(1);
    let pixel = clamp(vec2<i32>(uv * vec2<f32>(size)), vec2<i32>(0), max_pixel);
    return textureLoad(scene_depth, pixel, 0);
}

fn view_position(uv: vec2<f32>) -> vec3<f32> {
    let depth = depth_at(uv);
    let clip = vec4<f32>(
        uv.x * 2.0 - 1.0,
        1.0 - uv.y * 2.0,
        depth,
        1.0);
    let view = ssao_params.inverse_projection * clip;
    return view.xyz / max(abs(view.w), 0.000001);
}

fn reconstructed_normal(uv: vec2<f32>, center: vec3<f32>) -> vec3<f32> {
    // Use a broad fit for depth-derived normals. At distant builder-camera
    // positions a one/two-pixel baseline can be smaller than a Depth24 step,
    // turning a flat plane into horizontal ridges. Nearest-side selection
    // below still prevents this fit from crossing most silhouettes.
    let texel = 8.0 / vec2<f32>(textureDimensions(scene_depth));
    let left = view_position(uv - vec2<f32>(texel.x, 0.0));
    let right = view_position(uv + vec2<f32>(texel.x, 0.0));
    let up = view_position(uv - vec2<f32>(0.0, texel.y));
    let down = view_position(uv + vec2<f32>(0.0, texel.y));

    let dx_forward = right - center;
    let dx_backward = center - left;
    let dy_forward = up - center;
    let dy_backward = center - down;
    let dx = select(dx_backward, dx_forward, abs(dx_forward.z) < abs(dx_backward.z));
    let dy = select(dy_backward, dy_forward, abs(dy_forward.z) < abs(dy_backward.z));

    var normal = normalize(cross(dx, dy));
    if (dot(normal, -center) < 0.0) {
        normal = -normal;
    }
    return normal;
}

fn hash_u32(value: u32) -> u32 {
    var x = value;
    x = (x ^ 61u) ^ (x >> 16u);
    x = x + (x << 3u);
    x = x ^ (x >> 4u);
    x = x * 0x27d4eb2du;
    return x ^ (x >> 15u);
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let color = textureSample(source, source_sampler, input.uv);
    let center_depth = depth_at(input.uv);
    if (center_depth >= 0.999999) {
        return vec4<f32>(color.rgb, 1.0);
    }

    let center = view_position(input.uv);
    let normal = reconstructed_normal(input.uv, center);
    let radius = ssao_params.tuning.x;
    let strength = ssao_params.tuning.y;
    let bias = ssao_params.tuning.z;
    let max_darkening = ssao_params.tuning.w;
    let surface_thickness = ssao_params.projection_scale.z;
    let noise_floor = ssao_params.projection_scale.w;

    // Fill the projected neighborhood with a low-discrepancy Vogel disk. A
    // real integer hash rotates the disk independently per half-resolution AO
    // pixel, converting discrete sample contours into noise that the following
    // wide bilateral filter can remove.
    var occlusion = 0.0;
    let projected_radius = ssao_params.projection_scale.xy * radius / max(-center.z, 0.001);
    let pixel = vec2<u32>(input.position.xy);
    let random_bits = hash_u32(pixel.x ^ hash_u32(pixel.y));
    let rotation = f32(random_bits & 0x00ffffffu) * (6.2831853 / 16777216.0);
    for (var i = 0u; i < 32u; i++) {
        let sample_index = f32(i) + 0.5;
        let disk_radius = sqrt(sample_index / 32.0);
        let angle = rotation + sample_index * 2.39996323;
        let disk_offset = vec2<f32>(cos(angle), sin(angle)) * disk_radius;
        let sample_uv = input.uv + disk_offset * projected_radius;

        if (all(sample_uv > vec2<f32>(0.0)) && all(sample_uv < vec2<f32>(1.0))) {
            let sample_position = view_position(sample_uv);
            let delta = sample_position - center;
            let distance_to_sample = length(delta);
            if (distance_to_sample > 0.0001 && distance_to_sample < radius) {
                // Reject nearly coplanar samples before normalizing. Small
                // depth/normal errors otherwise turn an open plane into weak,
                // randomized self-occlusion across the entire screen.
                let elevation = dot(normal, delta) - surface_thickness;
                let facing = max(elevation / distance_to_sample - bias, 0.0);
                let range_weight = 1.0 - smoothstep(radius * 0.1, radius, distance_to_sample);
                occlusion += facing * range_weight;
            }
        }
    }

    let raw_occlusion = (occlusion / 32.0) * strength;
    let stable_occlusion = max(raw_occlusion - noise_floor, 0.0);
    let ao = 1.0 - min(stable_occlusion, max_darkening);
    return vec4<f32>(vec3<f32>(ao), 1.0);
}
