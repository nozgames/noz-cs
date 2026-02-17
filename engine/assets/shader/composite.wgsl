//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Composite shader - renders fullscreen quad with Y flip for final output

// Bind group 0: Texture
@group(0) @binding(0) var scene_texture: texture_2d<f32>;
@group(0) @binding(1) var scene_sampler: sampler;

// Vertex input (must declare all MeshVertex attributes for format hash compatibility)
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
}

// Vertex output / Fragment input
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = vec4<f32>(input.position, 0.0, 1.0);
    output.uv = input.uv;
    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    return textureSample(scene_texture, scene_sampler, input.uv);
}
