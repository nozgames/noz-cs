//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public static class Gizmos
{
    private const float DefaultLineWidth = 0.015f;
    private const float DefaultVertexSize = 0.12f;
    private const float DashedLineSegmentLength = 0.1f;
    private const int MinCircleSegments = 8;
    private const int MaxCircleSegments = 64;
    private const float MaxZoomRefScale = 1.0f;

    public static float ZoomRefScale => MathF.Min(1f / Workspace.Zoom, MaxZoomRefScale);

    public static void SetColor(Color color) => Graphics.SetColor(color);
    
    public static Graphics.AutoState PushState(ushort layer)
    {
        var state = Graphics.PushState();
        Graphics.SetLayer(layer);
        Graphics.SetTexture(Workspace.WhiteTexture);
        Graphics.SetShader(EditorAssets.Shaders.Texture);
        return state;
    }

    public static void DrawRect(Document doc, float width = 1.0f)
    {
        DrawRect(doc.Bounds, width);
    }

    public static void DrawRect(Rect rect, float width = 1.0f, ushort order=0, bool outside=false)
    {
        var topLeft = new Vector2(rect.Left, rect.Top);
        var topRight = new Vector2(rect.Right, rect.Top);
        var bottomLeft = new Vector2(rect.Left, rect.Bottom);
        var bottomRight = new Vector2(rect.Right, rect.Bottom);

        if (outside)
        {
            var scaledWidth = width * ZoomRefScale;
            topLeft += new Vector2(-scaledWidth, -scaledWidth);
            topRight += new Vector2(scaledWidth, -scaledWidth);
            bottomLeft += new Vector2(-scaledWidth, scaledWidth);
            bottomRight += new Vector2(scaledWidth, scaledWidth);
        }

        DrawLine(topLeft, topRight, width, extendEnds: true, order: order);
        DrawLine(topRight, bottomRight, width, extendEnds: true, order: order);
        DrawLine(bottomRight, bottomLeft, width, extendEnds: true, order: order);
        DrawLine(bottomLeft, topLeft, width, extendEnds: true, order: order);
    }

    public static void DrawRotatedRect(Rect bounds, float rotation, float width = 1.0f, ushort order = 0)
    {
        Graphics.PushState();
        Graphics.SetTransform(Matrix3x2.CreateRotation(rotation) * Graphics.Transform);
        DrawRect(bounds, width, order);
        Graphics.PopState();
    }

    public static void DrawRotatedRect(Rect bounds, Vector2 pivot, float rotation, float width = 1.0f, ushort order = 0)
    {
        Graphics.PushState();
        Graphics.SetTransform(Matrix3x2.CreateRotation(rotation, pivot) * Graphics.Transform);
        DrawRect(bounds, width, order);
        Graphics.PopState();
    }

    public static void DrawLine(Vector2 v0, Vector2 v1, float width, bool extendEnds = false, ushort order=0)
    {
        var delta = v1 - v0;
        var length = delta.Length();
        if (length < 0.0001f)
            return;

        var dir = delta / length;
        var perp = new Vector2(-dir.Y, dir.X);
        var halfWidth = width * ZoomRefScale;

        var start = v0;
        var end = v1;
        if (extendEnds)
        {
            start -= dir * halfWidth;
            end += dir * halfWidth;
        }

        var p0 = start - perp * halfWidth;
        var p1 = start + perp * halfWidth;
        var p2 = end + perp * halfWidth;
        var p3 = end - perp * halfWidth;

        Graphics.Draw(p0, p1, p2, p3, order);
    }

    public static void DrawRect(in Vector2 position, float size, ushort order=0)
    {
        var scaledSize = ZoomRefScale * size;
        var halfSize = scaledSize * 0.5f;
        Graphics.Draw(position.X - halfSize, position.Y - halfSize, scaledSize, scaledSize, order);
    }

    public static void DrawCircle(Vector2 center, float size, ushort order = 0)
    {
        var radius = size * 0.5f * ZoomRefScale;
        var segmentRatio = MathEx.Clamp01((Graphics.Camera?.WorldToScreen(radius) ?? 0.0f) / 96.0f);
        var segments = (int)float.Lerp(
            MinCircleSegments,
            MaxCircleSegments,
            segmentRatio);

        Span<MeshVertex> verts = stackalloc MeshVertex[segments + 1];
        Span<ushort> indices = stackalloc ushort[segments * 3];

        verts[0] = new MeshVertex { Position = center };

        var angleStep = MathF.PI * 2f / segments;
        for (var i = 0; i < segments; i++)
        {
            var angle = i * angleStep;
            verts[i + 1] = new MeshVertex
            {
                Position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius
            };
        }

        for (var i = 0; i < segments; i++)
        {
            indices[i * 3 + 0] = 0;
            indices[i * 3 + 1] = (ushort)(i + 1);
            indices[i * 3 + 2] = (ushort)((i + 1) % segments + 1);
        }

        Graphics.AddTriangles(verts, indices, order);
    }
    
    public static float GetVertexSize(float size=1.0f)
    {
        return DefaultVertexSize * size;
    }
    
    public static float GetLineWidth(float width=1.0f)
    {
        return DefaultLineWidth * width;
    }

    public static void DrawBone(
        Vector2 start,
        Vector2 end,
        float width,
        Color color,
        ushort order = 0)
    {
        const int CircleSegments = 16;
        const float OutlineScale = 1.3f;

        var delta = end - start;
        var length = delta.Length();
        if (length < 0.0001f)
            return;

        var circleRadius = EditorStyle.Skeleton.BoneSize * ZoomRefScale;
        var vertCount = 1 + CircleSegments + 1;
        var triCount = CircleSegments + 2;

        Span<MeshVertex> verts = stackalloc MeshVertex[vertCount];
        Span<ushort> indices = stackalloc ushort[triCount * 3];

        var dir = delta / length;
        var angle = MathF.Atan2(dir.Y, dir.X);
        var topIdx = (ushort)(CircleSegments / 4 + 1);
        var botIdx = (ushort)(3 * CircleSegments / 4 + 1);
        var tipIdx = (ushort)(vertCount - 1);

        Graphics.PushState();
        Graphics.SetTransform(Matrix3x2.CreateRotation(angle, start) * Graphics.Transform);

        // Build outline mesh
        var outlineRadius = circleRadius * OutlineScale;
        verts[0] = new MeshVertex { Position = start };
        var angleStep = MathF.PI * 2f / CircleSegments;
        for (var i = 0; i < CircleSegments; i++)
        {
            var a = i * angleStep;
            verts[i + 1] = new MeshVertex
            {
                Position = start + new Vector2(MathF.Cos(a) * outlineRadius, MathF.Sin(a) * outlineRadius)
            };
        }
        verts[vertCount - 1] = new MeshVertex { Position = start + new Vector2(length, 0) };

        for (var i = 0; i < CircleSegments; i++)
        {
            indices[i * 3 + 0] = 0;
            indices[i * 3 + 1] = (ushort)(i + 1);
            indices[i * 3 + 2] = (ushort)((i + 1) % CircleSegments + 1);
        }

        var baseIdx = CircleSegments * 3;
        indices[baseIdx + 0] = topIdx;
        indices[baseIdx + 1] = tipIdx;
        indices[baseIdx + 2] = 0;
        indices[baseIdx + 3] = 0;
        indices[baseIdx + 4] = tipIdx;
        indices[baseIdx + 5] = botIdx;

        Graphics.SetColor(EditorStyle.Skeleton.BoneOriginColor);
        Graphics.AddTriangles(verts, indices, order);

        // Build fill mesh
        verts[0] = new MeshVertex { Position = start };
        for (var i = 0; i < CircleSegments; i++)
        {
            var a = i * angleStep;
            verts[i + 1] = new MeshVertex
            {
                Position = start + new Vector2(MathF.Cos(a) * circleRadius, MathF.Sin(a) * circleRadius)
            };
        }
        verts[vertCount - 1] = new MeshVertex { Position = start + new Vector2(length, 0) };

        Graphics.SetColor(color);
        Graphics.AddTriangles(verts, indices, (ushort)(order + 1));
        Graphics.PopState();

        Graphics.SetColor(EditorStyle.Skeleton.BoneOriginColor);
        DrawCircle(start, EditorStyle.Skeleton.BoneOriginSize, (ushort)(order + 2));
    }

    public static void DrawDashedLine(Vector2 start, Vector2 end, ushort order=0)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length < 0.0001f)
            return;

        var dir = delta / length;
        var pos = 0f;
        var drawing = true;
        var dashLength = DashedLineSegmentLength * ZoomRefScale;

        while (pos < length)
        {
            var segmentEnd = MathF.Min(pos + dashLength, length);
            if (drawing)
            {
                var p0 = start + dir * pos;
                var p1 = start + dir * segmentEnd;
                DrawLine(p0, p1, DefaultLineWidth, order: order);
            }
            pos = segmentEnd;
            drawing = !drawing;
        }
    }

    public static void DrawOrigin(Color color, ushort order=0)
    {
        SetColor(color);
        DrawRect(Vector2.Zero, EditorStyle.Workspace.OriginSize, order);
    }
}
