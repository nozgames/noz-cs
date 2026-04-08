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
    private const float MaxZoomRefScale = 0.8f;

    public static float ZoomRefScale => MathF.Min(1f / Workspace.Zoom, MaxZoomRefScale);

    public static void SetColor(Color color) => Graphics.SetColor(color);
    
    public static Graphics.AutoState PushState(ushort layer)
    {
        var state = Graphics.PushState();
        Graphics.SetLayer(layer);
        Graphics.SetTexture(Graphics.WhiteTexture);
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

    public static void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, ushort order = 0)
    {
        ReadOnlySpan<MeshVertex> verts =
        [
            new MeshVertex { Position = a, UV = Vector2.Zero, Color = Color.White },
            new MeshVertex { Position = b, UV = Vector2.Zero, Color = Color.White },
            new MeshVertex { Position = c, UV = Vector2.Zero, Color = Color.White },
        ];
        ReadOnlySpan<ushort> indices = [0, 1, 2];
        Graphics.Draw(verts, indices, order);
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

        verts[0] = new MeshVertex { Position = center, Color = Color.White };

        var angleStep = MathF.PI * 2f / segments;
        for (var i = 0; i < segments; i++)
        {
            var angle = i * angleStep;
            verts[i + 1] = new MeshVertex
            {
                Position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius,
                Color = Color.White
            };
        }

        for (var i = 0; i < segments; i++)
        {
            indices[i * 3 + 0] = 0;
            indices[i * 3 + 1] = (ushort)(i + 1);
            indices[i * 3 + 2] = (ushort)((i + 1) % segments + 1);
        }

        Graphics.Draw(verts, indices, order);
    }

    public static void DrawAnchor(Vector2 center, bool selected, float scale = 1.0f, ushort order = 0)
    {
        Sprite sprite = EditorAssets.Sprites.GizmoHandle;
        var baseSize = EditorStyle.SpritePath.AnchorSize * scale * ZoomRefScale;
        var ppu = sprite.PixelsPerUnit / sprite.Bounds.Width;

        using (Graphics.PushState())
        {
            Graphics.SetShader(EditorAssets.Shaders.Sprite);
            var baseTransform = Matrix3x2.CreateTranslation(center) * Graphics.Transform;

            // Outline (full size)
            Graphics.SetTransform(Matrix3x2.CreateScale(baseSize * ppu) * baseTransform);
            Graphics.SetTextureFilter(TextureFilter.Linear);
            Graphics.SetColor(EditorStyle.Palette.Primary);
            Graphics.Draw(sprite, order);

            // Fill (inset by outline width)
            if (!selected)
            {
                var fillSize = (baseSize - EditorStyle.SpritePath.AnchorOutlineSize * ZoomRefScale) * ppu;
                Graphics.SetTransform(Matrix3x2.CreateScale(fillSize) * baseTransform);
                Graphics.SetTextureFilter(TextureFilter.Linear);
                Graphics.SetColor(Color.White);
                Graphics.Draw(sprite, (ushort)(order + 1));
            }
        }
    }

    public static void DrawPieChart(Vector2 center, float size, ReadOnlySpan<float> weights, ReadOnlySpan<Color> colors, ushort order = 0)
    {
        var radius = size * 0.5f * ZoomRefScale;
        var segmentRatio = MathEx.Clamp01((Graphics.Camera?.WorldToScreen(radius) ?? 0.0f) / 96.0f);
        var totalSegments = (int)float.Lerp(MinCircleSegments, MaxCircleSegments, segmentRatio);

        // Count active weights
        var activeCount = 0;
        var totalWeight = 0f;
        for (var i = 0; i < weights.Length; i++)
        {
            if (weights[i] > 0.001f)
            {
                activeCount++;
                totalWeight += weights[i];
            }
        }

        if (activeCount == 0 || totalWeight < 0.001f)
            return;

        // Single weight: draw solid circle
        if (activeCount == 1)
        {
            for (var i = 0; i < weights.Length; i++)
            {
                if (weights[i] > 0.001f)
                {
                    Graphics.SetColor(colors[i]);
                    DrawCircle(center, size, order);
                    return;
                }
            }
        }

        // Multiple weights: draw pie wedges
        Graphics.SetColor(Color.White);
        var angleOffset = -MathF.PI * 0.5f; // start at top

        Span<MeshVertex> vertsBuf = stackalloc MeshVertex[totalSegments + 2];
        Span<ushort> indicesBuf = stackalloc ushort[totalSegments * 3];

        for (var wi = 0; wi < weights.Length; wi++)
        {
            if (weights[wi] <= 0.001f)
                continue;

            var wedgeAngle = weights[wi] / totalWeight * MathF.PI * 2f;
            var wedgeSegments = int.Max(2, (int)(totalSegments * weights[wi] / totalWeight));

            var verts = vertsBuf[..(wedgeSegments + 2)];
            var indices = indicesBuf[..(wedgeSegments * 3)];

            verts[0] = new MeshVertex { Position = center, Color = colors[wi] };

            var angleStep = wedgeAngle / wedgeSegments;
            for (var i = 0; i <= wedgeSegments; i++)
            {
                var angle = angleOffset + i * angleStep;
                verts[i + 1] = new MeshVertex
                {
                    Position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius,
                    Color = colors[wi]
                };
            }

            for (var i = 0; i < wedgeSegments; i++)
            {
                indices[i * 3 + 0] = 0;
                indices[i * 3 + 1] = (ushort)(i + 1);
                indices[i * 3 + 2] = (ushort)(i + 2);
            }

            Graphics.Draw(verts, indices, order);
            angleOffset += wedgeAngle;
        }
    }

    public static float GetVertexSize(float size=1.0f)
    {
        return DefaultVertexSize * size;
    }
    
    public static float GetLineWidth(float width=1.0f)
    {
        return DefaultLineWidth * width;
    }    

    public static void DrawJoint(Vector2 position, bool selected=false)
    {
        SetColor(selected ? EditorStyle.Skeleton.SelectedBoneColor : EditorStyle.Skeleton.JointColor );
        DrawCircle(position, EditorStyle.Skeleton.JointSize, order: 1);
    }

    public static void DrawBone(SkeletonDocument skeleton, int boneIndex, bool selected=false)
    {
        ref var m = ref skeleton.LocalToWorld[boneIndex];
        var bone = skeleton.Bones[boneIndex];
        var p0 = Vector2.Transform(Vector2.Zero, m);
        var p1 = Vector2.Transform(new Vector2(bone.Length, 0), m);
        DrawBone(p0, p1, selected: selected);
    }

    public static void DrawBoneAndJoints(SkeletonDocument skeleton, int boneIndex, bool selected = false)
    {
        ref var m = ref skeleton.LocalToWorld[boneIndex];
        var bone = skeleton.Bones[boneIndex];
        var p0 = Vector2.Transform(Vector2.Zero, m);
        var p1 = Vector2.Transform(new Vector2(bone.Length, 0), m);
        DrawBone(p0, p1, selected: selected);
        DrawJoint(p0, selected: selected);
        DrawJoint(p1, selected: selected);
    }

    public static void DrawBone(
        Vector2 start,
        Vector2 end,
        bool selected=false)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length < 0.0001f)
            return;

        SetColor(selected ? EditorStyle.Skeleton.SelectedBoneColor : EditorStyle.Skeleton.BoneColor);

        var dir = Vector2.Normalize(end - start);
        var normal = new Vector2(-dir.Y, dir.X);
        var b = Vector2.Lerp(start, end, EditorStyle.Skeleton.BoneBaseRatio);
        var p0 = b + normal * length * EditorStyle.Skeleton.BoneBaseRatio;
        var p1 = b - normal * length * EditorStyle.Skeleton.BoneBaseRatio;

        DrawLine(p0, end, EditorStyle.Skeleton.BoneLineWidth);
        DrawLine(p1, end, EditorStyle.Skeleton.BoneLineWidth);
        DrawLine(p0, start, EditorStyle.Skeleton.BoneLineWidth);
        DrawLine(p1, start, EditorStyle.Skeleton.BoneLineWidth);
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

    public static void DrawBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        float width = DefaultLineWidth, int segments = 24, ushort order = 0)
    {
        var prev = p0;
        for (var i = 1; i <= segments; i++)
        {
            var t = i / (float)segments;
            var u = 1f - t;
            var point = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
            DrawLine(prev, point, width, order: order);
            prev = point;
        }
    }

    public static void DrawOrigin(Color color, ushort order=0)
    {
        SetColor(color);
        DrawRect(Vector2.Zero, EditorStyle.Workspace.OriginSize, order);
    }

    public static void DrawArc(Vector2 center, float radius, float startAngle, float endAngle, float lineWidth, ushort order = 0)
    {
        var screenRadius = Graphics.Camera?.WorldToScreen(radius) ?? 0.0f;
        var segmentRatio = MathEx.Clamp01(screenRadius / 96.0f);
        var totalSegments = (int)float.Lerp(MinCircleSegments, MaxCircleSegments, segmentRatio);
        var angleSpan = endAngle - startAngle;
        var segments = int.Max(4, (int)(totalSegments * MathF.Abs(angleSpan) / (MathF.PI * 2f)));

        var prev = center + new Vector2(MathF.Cos(startAngle), MathF.Sin(startAngle)) * radius;
        for (var i = 1; i <= segments; i++)
        {
            var angle = startAngle + angleSpan * i / segments;
            var curr = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            DrawLine(prev, curr, lineWidth, order: order);
            prev = curr;
        }
    }

    public static void DrawEnvelope(Vector2 head, Vector2 tail, float headRadius, float tailRadius, ushort order = 0)
    {
        var delta = tail - head;
        var length = delta.Length();
        if (length < 0.0001f)
            return;

        var dir = delta / length;
        var normal = new Vector2(-dir.Y, dir.X);
        var lineWidth = EditorStyle.Skeleton.EnvelopeLineWidth;
        var boneAngle = MathF.Atan2(dir.Y, dir.X);

        // Tangent angle offset for tapered capsule
        var rDiff = headRadius - tailRadius;
        var tangentAngle = MathF.Abs(rDiff) < length ? MathF.Asin(rDiff / length) : 0f;

        // Tangent contact angles
        var angleTop = boneAngle + MathF.PI / 2f + tangentAngle;
        var angleBot = boneAngle - MathF.PI / 2f + tangentAngle;

        // Tangent line endpoints
        var headTop = head + new Vector2(MathF.Cos(angleTop), MathF.Sin(angleTop)) * headRadius;
        var headBot = head + new Vector2(MathF.Cos(angleBot), MathF.Sin(angleBot)) * headRadius;
        var tailTop = tail + new Vector2(MathF.Cos(angleTop), MathF.Sin(angleTop)) * tailRadius;
        var tailBot = tail + new Vector2(MathF.Cos(angleBot), MathF.Sin(angleBot)) * tailRadius;

        // Draw tangent lines
        if (headRadius > 0 || tailRadius > 0)
        {
            DrawLine(headTop, tailTop, lineWidth, order: order);
            DrawLine(headBot, tailBot, lineWidth, order: order);
        }

        // Draw arcs (semicircles facing outward)
        if (headRadius > 0)
        {
            var headArcStart = angleBot;
            var headArcEnd = angleTop - MathF.PI * 2f;
            DrawArc(head, headRadius, headArcEnd, headArcStart, lineWidth, order);
        }

        if (tailRadius > 0)
        {
            DrawArc(tail, tailRadius, angleTop, angleBot, lineWidth, order);
        }
    }

    #region Hit Testing

    public static bool HitTestBone(Vector2 head, Vector2 tail, Vector2 point)
    {
        var delta = tail - head;
        var length = delta.Length();
        if (length < 0.0001f)
            return Vector2.Distance(point, head) <= EditorStyle.Skeleton.BoneHitThreshold * ZoomRefScale;

        // Compute diamond vertices (matching DrawBone)
        var dir = delta / length;
        var normal = new Vector2(-dir.Y, dir.X);
        var baseRatio = EditorStyle.Skeleton.BoneBaseRatio;
        var b = Vector2.Lerp(head, tail, baseRatio);
        var halfWidth = length * baseRatio;
        var p0 = b + normal * halfWidth;
        var p1 = b - normal * halfWidth;

        // Expand diamond outward by threshold
        var threshold = EditorStyle.Skeleton.BoneHitThreshold * ZoomRefScale;
        var headScaled = head - dir * threshold;
        var tailScaled = tail + dir * threshold;
        var p0Scaled = p0 + normal * threshold;
        var p1Scaled = p1 - normal * threshold;

        // CCW winding: head → p1 → tail → p0
        Span<Vector2> diamond = [headScaled, p1Scaled, tailScaled, p0Scaled];
        return Physics.OverlapPoint(diamond, point);
    }

    public static bool HitTestJoint(Vector2 position, Vector2 point)
    {
        var radius = EditorStyle.Skeleton.JointHitSize * ZoomRefScale;
        return Vector2.DistanceSquared(point, position) <= radius * radius;
    }

    #endregion
}
