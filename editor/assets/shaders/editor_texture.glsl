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

layout(location = 0) out vec2 f_uv;

void main() {
    vec3 world_pos = vec3(v_position, 1.0) * object.transform;
    mat3 mvp = object.transform * camera.view_projection;
    vec3 screen_pos = vec3(v_position, 1.0) * mvp;
    float depth = (object.depth + (v_depth * object.depth_scale) - object.depth_min) / (object.depth_max - object.depth_min);
    gl_Position = vec4(screen_pos.xy, 1.0f - depth, 1.0);
    f_uv = v_uv;
}

//@ END

//@ FRAGMENT

layout(location = 0) in vec2 f_uv;
layout(location = 1) in vec2 f_world_pos;
layout(location = 2) flat in int f_is_animated;
layout(location = 3) flat in int f_frame_count;
layout(location = 4) flat in float f_frame_width_uv;
layout(location = 5) flat in float f_frame_rate;
layout(location = 6) flat in int f_atlas_index;
layout(location = 0) out vec4 outColor;

layout(set = 4, binding = 0) uniform ColorBuffer {
    vec4 color;
    vec4 emission;
    vec2 uv_offset;
    vec2 padding;
} color_buffer;

layout(set = 6, binding = 0) uniform sampler2D mainTexture;

void main() {
    outColor = texture(mainTexture, f_uv) * color_buffer.color;
}

//@ END
