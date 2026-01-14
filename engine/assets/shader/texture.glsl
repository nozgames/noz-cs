//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Simple texture shader - uses sampler2D, no texture array
//  For editor document rendering (textures, sprite editor)
//

#version 450

//@ VERTEX

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;
layout(location = 3) in vec4 in_color;

uniform mat4 u_projection;

out vec2 v_uv;
out vec4 v_color;

void main() 
{
    gl_Position = u_projection * vec4(in_position, 0.0, 1.0);
    v_uv = in_uv;
    v_color = in_color;
}

//@ END

//@ FRAGMENT

in vec2 v_uv;
in vec4 v_color;

uniform sampler2D sampler_texture;

out vec4 f_color;

void main() 
{
    f_color = texture(sampler_texture, v_uv) * v_color;
}

//@ END
