// Preserve post-processed color while restoring opaque depth for later geometry.
struct Globals { projection: mat4x4<f32>, time: f32, }
@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var color_texture: texture_2d<f32>;
@group(0) @binding(2) var color_sampler: sampler;
@group(0) @binding(3) var scene_depth: texture_depth_2d;

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
struct FragmentOutput {
    @location(0) color: vec4<f32>,
    @builtin(frag_depth) depth: f32,
}
@vertex fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = vec4<f32>(input.uv * vec2<f32>(2, -2) + vec2<f32>(-1, 1), 0, 1);
    output.uv = input.uv;
    return output;
}
@fragment fn fs_main(input: VertexOutput) -> FragmentOutput {
    var output: FragmentOutput;
    output.color = textureSample(color_texture, color_sampler, input.uv);
    let pixel = clamp(vec2<i32>(input.uv * vec2<f32>(textureDimensions(scene_depth))),
        vec2<i32>(0), vec2<i32>(textureDimensions(scene_depth)) - 1);
    output.depth = textureLoad(scene_depth, pixel, 0);
    return output;
}
