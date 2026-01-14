//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#version 450

//@ VERTEX

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;
layout(location = 2) in vec2 in_normal;
layout(location = 3) in vec4 in_color;
layout(location = 4) in int in_bone;
layout(location = 5) in int in_atlas;
layout(location = 6) in int in_frame_count;
layout(location = 7) in float in_frame_width;
layout(location = 8) in float in_frame_rate;
layout(location = 9) in float in_frame_time;

uniform mat4 u_projection;
uniform float u_time;
uniform vec3 u_bones[128];

out vec2 v_uv;
out vec4 v_color;
flat out int v_atlas;

void main() 
{
    // Get bone transform (2 vec3s per bone)
    int boneIdx = in_bone * 2;
    vec3 boneCol0 = u_bones[boneIdx];
    vec3 boneCol1 = u_bones[boneIdx + 1];

    // Apply bone transform: pos' = M * pos (2D affine)
    vec2 skinnedPos = vec2(
        boneCol0.x * in_position.x + boneCol1.x * in_position.y + boneCol0.z,
        boneCol0.y * in_position.x + boneCol1.y * in_position.y + boneCol1.z
    );

    // Animation: offset UV.x based on current frame
    vec2 uv = in_uv;
    if (in_frame_count > 1) {
        float animTime = u_time - in_frame_time;
        float totalFrames = float(in_frame_count);
        float currentFrame = floor(mod(animTime * in_frame_rate, totalFrames));
        uv.x += currentFrame * in_frame_width;
    }

    gl_Position = u_projection * vec4(skinnedPos, 0.0, 1.0);
    v_uv = uv;
    v_color = in_color;
    v_atlas = in_atlas;
}

//@ END

//@ FRAGMENT

in vec2 v_uv;
in vec4 v_color;
flat in int v_atlas;

uniform sampler2D sampler_texture;

out vec4 f_color;

void main() 
{
    f_color = texture(sampler_texture, v_uv) * v_color;
}

//@ END
