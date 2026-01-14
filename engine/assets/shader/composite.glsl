//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  UI composite shader - renders fullscreen quad with Y flip for final output

#version 450

//@ VERTEX

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;

out vec2 v_uv;

void main() 
{
    gl_Position = vec4(in_position, 0.0, 1.0);
    v_uv = vec2(in_uv.x, 1.0 - in_uv.y);
}

//@ END

//@ FRAGMENT

in vec2 v_uv;

uniform sampler2D sampler_texture;

out vec4 f_color;

void main() 
{
    f_color = texture(sampler_texture, v_uv);
}

//@ END
