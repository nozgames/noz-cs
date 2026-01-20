//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  UI Image shader - samples from texture array with atlas index

#version 450

//@ VERTEX

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;
layout(location = 2) in vec4 in_color;
layout(location = 3) in int in_atlas_index;

uniform mat4 u_projection;

out vec2 v_uv;
out vec4 v_color;
flat out int v_atlas_index;

void main() {
    gl_Position = u_projection * vec4(in_position, 0.0, 1.0);
    v_uv = in_uv;
    v_color = in_color;
    v_atlas_index = in_atlas_index;
}

//@ END

//@ FRAGMENT

in vec2 v_uv;
in vec4 v_color;
flat in int v_atlas_index;

out vec4 f_color;

uniform sampler2DArray u_texture;

void main() {
    vec4 tex = texture(u_texture, vec3(v_uv, float(v_atlas_index)));
    f_color = tex * v_color;
    if (f_color.a < 0.001) discard;
}

//@ END
