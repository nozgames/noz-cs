//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  UI shader - SDF rounded rectangles with borders

#version 450

//@ VERTEX

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;
layout(location = 3) in vec4 in_color;

uniform mat4 u_color;

// UI-specific uniforms
uniform vec2 uBoxSize;      // Size of the box in pixels
uniform float uBorderRadius; // Corner radius
uniform float uBorderWidth;  // Border thickness
uniform vec4 uBorderColor;   // Border color

out vec2 v_uv;
out vec4 v_color;
out vec2 vLocalPos; // Position within box (0-1)

void main() {
    gl_Position = u_color * vec4(in_position, 0.0, 1.0);
    v_uv = in_uv;
    v_color = in_color;
    vLocalPos = in_uv; // UV maps to box position
}

//@ END

//@ FRAGMENT

in vec2 v_uv;
in vec4 v_color;
in vec2 vLocalPos;

uniform vec2 uBoxSize;
uniform float uBorderRadius;
uniform float uBorderWidth;
uniform vec4 uBorderColor;

out vec4 FragColor;

// SDF for rounded rectangle
float sdRoundedBox(vec2 p, vec2 b, float r) {
    vec2 q = abs(p) - b + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void main() {
    // Convert UV (0-1) to centered coordinates (-halfSize to +halfSize)
    vec2 halfSize = uBoxSize * 0.5;
    vec2 p = (vLocalPos - 0.5) * uBoxSize;

    // Clamp border radius to half the smaller dimension
    float maxRadius = min(halfSize.x, halfSize.y);
    float radius = min(uBorderRadius, maxRadius);

    // Distance to rounded rectangle edge
    float dist = sdRoundedBox(p, halfSize, radius);

    // Anti-aliasing width (in pixels, ~1px smoothing)
    float aa = 1.0;

    // Fill: inside the shape
    float fillAlpha = 1.0 - smoothstep(-aa, 0.0, dist);

    // Border: between inner and outer edge
    float innerDist = dist + uBorderWidth;
    float borderAlpha = smoothstep(-aa, 0.0, innerDist) * (1.0 - smoothstep(-aa, 0.0, dist));

    // Combine fill and border
    vec4 fillColor = v_color;
    vec4 finalColor = mix(fillColor, uBorderColor, borderAlpha);
    finalColor.a *= fillAlpha;

    // If no fill color (transparent), only show border
    if (v_color.a < 0.001) {
        finalColor = uBorderColor;
        finalColor.a *= borderAlpha;
    }

    FragColor = finalColor;
}

//@ END
