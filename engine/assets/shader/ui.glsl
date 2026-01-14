//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  UI shader - SDF rounded rectangles with borders

#version 450

//@ VERTEX

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;
layout(location = 3) in vec4 in_color;

uniform mat4 u_projection;

// UI-specific uniforms
uniform vec2 u_box_size;      // Size of the box in pixels
uniform float u_border_radius; // Corner radius
uniform float u_border_width;  // Border thickness
uniform vec4 u_border_color;   // Border color

out vec2 v_uv;
out vec4 v_color;
out vec2 v_local_pos; // Position within box (0-1)

void main() {
    gl_Position = u_projection * vec4(in_position, 0.0, 1.0);
    v_uv = in_uv;
    v_color = in_color;
    v_local_pos = in_uv; // UV maps to box position
}

//@ END

//@ FRAGMENT

in vec2 v_uv;
in vec4 v_color;
in vec2 v_local_pos;

uniform vec2 u_box_size;
uniform float u_border_radius;
uniform float u_border_width;
uniform vec4 u_border_color;

out vec4 f_color;

// SDF for rounded rectangle
float sdRoundedBox(vec2 p, vec2 b, float r) {
    vec2 q = abs(p) - b + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void main() {
    // Convert UV (0-1) to centered coordinates (-halfSize to +halfSize)
    vec2 halfSize = u_box_size * 0.5;
    vec2 p = (v_local_pos - 0.5) * u_box_size;

    // Clamp border radius to half the smaller dimension
    float maxRadius = min(halfSize.x, halfSize.y);
    float radius = min(u_border_radius, maxRadius);

    // Distance to rounded rectangle edge
    float dist = sdRoundedBox(p, halfSize, radius);

    // Anti-aliasing width (in pixels, ~1px smoothing)
    float aa = 1.0;

    // Fill: inside the shape
    float fillAlpha = 1.0 - smoothstep(-aa, 0.0, dist);

    // Border: between inner and outer edge
    float innerDist = dist + u_border_width;
    float borderAlpha = smoothstep(-aa, 0.0, innerDist) * (1.0 - smoothstep(-aa, 0.0, dist));

    // Combine fill and border
    vec4 fillColor = v_color;
    vec4 finalColor = mix(fillColor, u_border_color, borderAlpha);
    finalColor.a *= fillAlpha;

    // If no fill color (transparent), only show border
    if (v_color.a < 0.001) {
        finalColor = u_border_color;
        finalColor.a *= borderAlpha;
    }

    f_color = finalColor;
}

//@ END
