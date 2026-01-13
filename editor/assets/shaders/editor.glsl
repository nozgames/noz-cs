#version 450

//@ VERTEX

layout(set = 0, binding = 0, row_major) uniform CameraBuffer {
    mat3 view_projection;
} camera;

layout(set = 1, binding = 0, row_major) uniform ObjectBuffer {
    mat3 transform;
    float depth;
    float depth_scale;
    float depth_min;
    float depth_max;
} object;

layout(location = 0) in vec2 v_position;
layout(location = 1) in vec2 v_uv;
layout(location = 2) in vec2 v_normal;
layout(location = 3) in vec4 v_color;
layout(location = 4) in float v_opacity;
layout(location = 5) in float v_depth;
layout(location = 6) in ivec4 v_bone_indices;
layout(location = 7) in vec4 v_bone_weights;
layout(location = 8) in int v_atlas_index;

layout(location = 0) out vec4 f_color;

void main() {
    mat3 mvp = object.transform * camera.view_projection;
    vec3 screen_pos = vec3(v_position, 1.0) * mvp;
    float depth = (object.depth + (v_depth * object.depth_scale) - object.depth_min) / (object.depth_max - object.depth_min);
    gl_Position = vec4(screen_pos.xy, 1.0f - depth, 1.0);
    f_color = v_color;
}

//@ END

//@ FRAGMENT

layout(location = 0) in vec4 f_color;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = f_color;
}

//@ END
