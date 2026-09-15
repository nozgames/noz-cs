// NoZ - Copyright(c) 2026 NoZ Games, LLC
struct Globals { projection: mat4x4<f32>, time: f32, }
@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var source: texture_2d<f32>;
struct Output { @builtin(position) position: vec4<f32>, @location(0) uv: vec2<f32>, }
@vertex fn vs_main(@location(0) position: vec3<f32>, @location(3) uv: vec2<f32>) -> Output {
    var o: Output; o.position = vec4<f32>(position, 1); o.uv = uv; return o;
}
@fragment fn fs_main(input: Output) -> @location(0) vec4<f32> {
    let size = vec2<i32>(textureDimensions(source));
    return textureLoad(source, clamp(vec2<i32>(input.uv * vec2<f32>(size)), vec2<i32>(0), size - 1), 0);
}
