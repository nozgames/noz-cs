struct Globals { projection: mat4x4<f32>, time: f32, }
@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var particle_texture: texture_2d<f32>;
@group(0) @binding(2) var particle_sampler: sampler;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) tangent: vec4<f32>,
    @location(3) uv: vec2<f32>,
    @location(4) color: vec4<f32>,
    @location(15) grunge_uvs: vec4<f32>, // Shared MeshVertex3D layout; unused in this pass.
}
struct Billboard {
    @location(5) position: vec3<f32>,
    @location(6) right: vec3<f32>,
    @location(7) up: vec3<f32>,
    @location(8) color: vec4<f32>,
    @location(9) uv: vec4<f32>,
}
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) color: vec4<f32>,
}
@vertex fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = globals.projection * vec4<f32>(input.position, 1.0);
    output.uv = input.uv;
    output.color = input.color;
    return output;
}
@vertex fn vs_instanced(input: VertexInput, particle: Billboard) -> VertexOutput {
    var output: VertexOutput;
    let world = particle.position + particle.right * input.position.x + particle.up * input.position.y;
    output.position = globals.projection * vec4<f32>(world, 1.0);
    output.uv = particle.uv.xy + input.uv * particle.uv.zw;
    output.color = particle.color;
    return output;
}
@fragment fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    return textureSample(particle_texture, particle_sampler, input.uv) * input.color;
}
