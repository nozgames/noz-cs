// NoZ - Copyright(c) 2026 NoZ Games, LLC
struct Globals {
    projection: mat4x4<f32>, time: f32,
    normal_to_world: mat4x4<f32>, model: mat4x4<f32>, light: vec4<f32>,
}
@group(0) @binding(0) var<uniform> globals: Globals;
struct Output { @builtin(position) position: vec4<f32>, @location(0) world: vec3<f32>, @location(1) opacity: f32, }
@vertex fn vs_main(@location(0) position: vec3<f32>, @location(4) color: vec4<f32>) -> Output {
    var o: Output;
    o.position = globals.projection * vec4<f32>(position, 1);
    o.world = (globals.model * vec4<f32>(position, 1)).xyz;
    o.opacity = color.a;
    return o;
}
@fragment fn fs_main(input: Output) -> @location(0) vec4<f32> {
    if (input.opacity < .5) { discard; }
    // Integer byte packing survives an RGBA8 atlas copy exactly. Sun uses the
    // depth attachment; points store linear radial distance for all six faces.
    let d = clamp(distance(input.world, globals.light.xyz) / max(globals.light.w, .001), 0.0, 1.0);
    let bits = u32(round(d * 16777215.0));
    return vec4<f32>(f32(bits & 255u), f32((bits >> 8u) & 255u), f32((bits >> 16u) & 255u), 255.0) / 255.0;
}
