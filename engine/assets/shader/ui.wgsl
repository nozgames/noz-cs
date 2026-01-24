//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  UI shader - SDF squircle with per-vertex border data

// Bind group 0: Globals
struct Globals {
    projection: mat4x4<f32>,
    time: f32,
}

@group(0) @binding(0) var<uniform> globals: Globals;

// Vertex input
struct VertexInput {
    @location(0) position: vec2<f32>,
    @location(1) uv: vec2<f32>,
    @location(2) normal: vec2<f32>,
    @location(3) color: vec4<f32>,
    @location(4) border_ratio: f32,
    @location(5) border_color: vec4<f32>,
}

// Vertex output / Fragment input
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) @interpolate(flat) border_ratio: f32,
    @location(3) @interpolate(flat) border_color: vec4<f32>,
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;

    var screen_pos = globals.projection * vec4<f32>(input.position, 0.0, 1.0);
    let scale_x = globals.projection[0][0];
    let scale_y = globals.projection[1][1];
    screen_pos.x += input.normal.x * scale_x;
    screen_pos.y += input.normal.y * scale_y;

    output.position = vec4<f32>(screen_pos.xy, 0.0, 1.0);
    output.uv = input.uv;
    output.color = input.color;
    output.border_ratio = input.border_ratio;
    output.border_color = input.border_color;

    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    if (input.border_ratio < 0.0) {
        return input.color;
    }

    let n = 4.0;
    let dist = pow(pow(abs(input.uv.x), n) + pow(abs(input.uv.y), n), 1.0 / n);
    let edge = fwidth(dist);

    var color = input.color;
    let border_color = input.border_color;

    let border = (1.0 + edge) - input.border_ratio;
    color = mix(color, border_color, smoothstep(border - edge, border, dist));
    color.a = (1.0 - smoothstep(1.0 - edge, 1.0, dist)) * color.a;

    if (color.a < 0.001) {
        discard;
    }

    return color;
}
