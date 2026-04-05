//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Anti-aliased scanline polygon rasterizer using signed-area coverage.
//  Takes Clipper2 PathsD contours (flat linear polygons in world-space
//  coordinates) and composites them into a PixelData<Color32> bitmap.
//
//  Algorithm overview (stb_truetype style):
//  For each edge, walk scanlines it overlaps. Within each scanline, compute
//  the signed area the edge contributes to each pixel column. A running sum
//  of these area deltas gives per-pixel winding coverage.
//

using System.Numerics;
using Clipper2Lib;

namespace NoZ.Editor;

internal static class Rasterizer
{
    [ThreadStatic] private static List<Edge>? _edgePool;
    [ThreadStatic] private static float[]? _coveragePool;

    private static readonly Comparison<Edge> EdgeYMinComparison = (a, b) => a.YMin.CompareTo(b.YMin);

    public static void Fill(
        PathsD paths,
        PixelData<Color32> target,
        RectInt targetRect,
        Vector2Int sourceOffset,
        int dpi,
        Color32 color)
    {
        int w = targetRect.Width;
        int h = targetRect.Height;
        if (w <= 0 || h <= 0 || paths.Count == 0) return;

        // Reuse pooled edge list
        var edges = _edgePool ??= new List<Edge>();
        edges.Clear();
        CollectEdges(edges, paths, dpi, sourceOffset);
        if (edges.Count == 0) return;

        edges.Sort(EdgeYMinComparison);

        // Reuse pooled coverage buffer, grow if needed
        var coverageLen = w + 2;
        var coverage = _coveragePool;
        if (coverage == null || coverage.Length < coverageLen)
        {
            coverage = new float[coverageLen];
            _coveragePool = coverage;
        }

        int edgeStart = 0;

        for (int py = 0; py < h; py++)
        {
            Array.Clear(coverage, 0, coverageLen);

            float rowTop = py;
            float rowBot = py + 1;

            for (int ei = edgeStart; ei < edges.Count; ei++)
            {
                var edge = edges[ei];

                if (edge.YMin >= rowBot) break;

                if (edge.YMax <= rowTop)
                {
                    if (ei == edgeStart) edgeStart++;
                    continue;
                }

                // Clip edge to scanline [rowTop, rowBot]
                float eyMin, eyMax, exAtMin, exAtMax;
                if (edge.Y0 < edge.Y1)
                {
                    eyMin = edge.Y0;
                    eyMax = edge.Y1;
                    exAtMin = edge.X0;
                    exAtMax = edge.X1;
                }
                else
                {
                    eyMin = edge.Y1;
                    eyMax = edge.Y0;
                    exAtMin = edge.X1;
                    exAtMax = edge.X0;
                }

                float clipTop = MathF.Max(eyMin, rowTop);
                float clipBot = MathF.Min(eyMax, rowBot);
                if (clipTop >= clipBot) continue;

                float edgeHeight = eyMax - eyMin;
                float invHeight = 1f / edgeHeight;
                float tTop = (clipTop - eyMin) * invHeight;
                float tBot = (clipBot - eyMin) * invHeight;
                float xAtTop = exAtMin + tTop * (exAtMax - exAtMin);
                float xAtBot = exAtMin + tBot * (exAtMax - exAtMin);

                float dy = clipBot - clipTop;
                float dir = edge.Direction;

                DepositEdge(coverage, w, xAtTop, xAtBot, dy * dir);
            }

            // Convert coverage to pixels via running sum
            int ty = targetRect.Y + py;
            float sum = 0;
            for (int px = 0; px < w; px++)
            {
                sum += coverage[px];
                float alpha = MathF.Abs(sum);
                if (alpha > 1f) alpha = 1f;

                if (alpha > 0.004f) // ~1/255
                {
                    int tx = targetRect.X + px;
                    byte srcA = (byte)(alpha * color.A + 0.5f);
                    var srcColor = new Color32(color.R, color.G, color.B, srcA);
                    var dst = target[tx, ty];
                    if (dst.A == 0)
                    {
                        target[tx, ty] = srcColor;
                    }
                    else
                    {
                        var blended = Color32.Blend(dst, srcColor);
                        // For opaque paths, use additive alpha (clamped) instead of
                        // Porter-Duff over. After geometry trimming, adjacent path
                        // coverages are complementary and should sum to full opacity.
                        // The over formula treats them as independent, causing dark seams.
                        if (color.A == 255)
                            blended.A = (byte)Math.Min(srcA + dst.A, 255);
                        target[tx, ty] = blended;
                    }
                }
            }
        }
    }

    public static void Fill(
        PathsD paths,
        PixelData<Color32> target,
        RectInt targetRect,
        Vector2Int sourceOffset,
        int dpi,
        SpriteFillType fillType,
        Color32 color,
        SpriteFillGradient gradient,
        Matrix3x2 gradientTransform)
    {
        if (fillType == SpriteFillType.Solid)
        {
            Fill(paths, target, targetRect, sourceOffset, dpi, color);
            return;
        }

        int w = targetRect.Width;
        int h = targetRect.Height;
        if (w <= 0 || h <= 0 || paths.Count == 0) return;

        var edges = _edgePool ??= new List<Edge>();
        edges.Clear();
        CollectEdges(edges, paths, dpi, sourceOffset);
        if (edges.Count == 0) return;

        edges.Sort(EdgeYMinComparison);

        var coverageLen = w + 2;
        var coverage = _coveragePool;
        if (coverage == null || coverage.Length < coverageLen)
        {
            coverage = new float[coverageLen];
            _coveragePool = coverage;
        }

        // Pre-compute gradient in pixel space
        var gs = Vector2.Transform(gradient.Start, gradientTransform);
        var ge = Vector2.Transform(gradient.End, gradientTransform);
        var gradStartPx = new Vector2((float)(gs.X * dpi) + sourceOffset.X, (float)(gs.Y * dpi) + sourceOffset.Y);
        var gradEndPx = new Vector2((float)(ge.X * dpi) + sourceOffset.X, (float)(ge.Y * dpi) + sourceOffset.Y);
        var axis = gradEndPx - gradStartPx;
        var axisSqLen = Vector2.Dot(axis, axis);
        var invAxisSqLen = axisSqLen > 0 ? 1f / axisSqLen : 0f;

        int edgeStart = 0;

        for (int py = 0; py < h; py++)
        {
            Array.Clear(coverage, 0, coverageLen);

            float rowTop = py;
            float rowBot = py + 1;

            for (int ei = edgeStart; ei < edges.Count; ei++)
            {
                var edge = edges[ei];
                if (edge.YMin >= rowBot) break;
                if (edge.YMax <= rowTop)
                {
                    if (ei == edgeStart) edgeStart++;
                    continue;
                }

                float eyMin, eyMax, exAtMin, exAtMax;
                if (edge.Y0 < edge.Y1)
                { eyMin = edge.Y0; eyMax = edge.Y1; exAtMin = edge.X0; exAtMax = edge.X1; }
                else
                { eyMin = edge.Y1; eyMax = edge.Y0; exAtMin = edge.X1; exAtMax = edge.X0; }

                float clipTop = MathF.Max(eyMin, rowTop);
                float clipBot = MathF.Min(eyMax, rowBot);
                if (clipTop >= clipBot) continue;

                float edgeHeight = eyMax - eyMin;
                float invHeight = 1f / edgeHeight;
                float tTop = (clipTop - eyMin) * invHeight;
                float tBot = (clipBot - eyMin) * invHeight;
                float xAtTop = exAtMin + tTop * (exAtMax - exAtMin);
                float xAtBot = exAtMin + tBot * (exAtMax - exAtMin);

                float dy = clipBot - clipTop;
                float dir = edge.Direction;

                DepositEdge(coverage, w, xAtTop, xAtBot, dy * dir);
            }

            int ty = targetRect.Y + py;
            float sum = 0;
            for (int px = 0; px < w; px++)
            {
                sum += coverage[px];
                float alpha = MathF.Abs(sum);
                if (alpha > 1f) alpha = 1f;

                if (alpha > 0.004f)
                {
                    int tx = targetRect.X + px;

                    // Compute gradient color for this pixel
                    var pixelPos = new Vector2(px, py);
                    float t = axisSqLen > 0 ? MathF.Max(0, MathF.Min(1, Vector2.Dot(pixelPos - gradStartPx, axis) * invAxisSqLen)) : 0;
                    var gradColor = Color32.Mix(gradient.StartColor, gradient.EndColor, t);

                    byte srcA = (byte)(alpha * gradColor.A + 0.5f);
                    var srcColor = new Color32(gradColor.R, gradColor.G, gradColor.B, srcA);
                    var dst = target[tx, ty];
                    if (dst.A == 0)
                    {
                        target[tx, ty] = srcColor;
                    }
                    else
                    {
                        var blended = Color32.Blend(dst, srcColor);
                        if (gradColor.A == 255)
                            blended.A = (byte)Math.Min(srcA + dst.A, 255);
                        target[tx, ty] = blended;
                    }
                }
            }
        }
    }

    // Deposit the signed area contribution of one edge segment into the coverage buffer.
    //
    // The edge goes from (x0, top) to (x1, bottom) within a single scanline row.
    // signedDy = (bottom - top) * direction, where direction is +1 (downward) or -1 (upward).
    //
    // Uses the stb_truetype approach: for an edge at x within pixel ix,
    // the signed area delta at pixel ix = (ix + 1 - xMid) * signedDy.
    private static void DepositEdge(float[] coverage, int w, float x0, float x1, float signedDy)
    {
        float xLeft = MathF.Min(x0, x1);
        float xRight = MathF.Max(x0, x1);

        int iLeft = Math.Max((int)MathF.Floor(xLeft), 0);
        int iRight = Math.Min((int)MathF.Floor(xRight), w - 1);

        if (iLeft == iRight)
        {
            // Edge stays within one pixel column
            float xMid = (x0 + x1) * 0.5f;
            int ix = Math.Max((int)MathF.Floor(xMid), 0);
            ix = Math.Min(ix, w - 1);

            float area = (ix + 1 - xMid) * signedDy;
            coverage[ix] += area;
            coverage[ix + 1] += signedDy - area;
        }
        else
        {
            // Edge spans multiple pixel columns — distribute area proportionally
            float invXSpan = 1f / (xRight - xLeft);

            for (int ix = iLeft; ix <= iRight; ix++)
            {
                float pxL = MathF.Max(xLeft, (float)ix);
                float pxR = MathF.Min(xRight, (float)(ix + 1));
                float spanFrac = (pxR - pxL) * invXSpan;

                float xMid = (pxL + pxR) * 0.5f;

                float h = spanFrac * signedDy;
                float area = (ix + 1 - xMid) * h;

                if (ix >= 0 && ix < w)
                    coverage[ix] += area;
                if (ix + 1 < w + 2)
                    coverage[ix + 1] += h - area;
            }
        }
    }

    private struct Edge
    {
        public float X0, Y0;
        public float X1, Y1;
        public float YMin, YMax;
        public float Direction; // +1 if going down (Y0 < Y1), -1 if going up
    }

    private static void CollectEdges(List<Edge> edges, PathsD paths, int dpi, Vector2Int sourceOffset)
    {
        foreach (var path in paths)
        {
            int count = path.Count;
            if (count < 3) continue;

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                float x0 = (float)(path[i].x * dpi) + sourceOffset.X;
                float y0 = (float)(path[i].y * dpi) + sourceOffset.Y;
                float x1 = (float)(path[j].x * dpi) + sourceOffset.X;
                float y1 = (float)(path[j].y * dpi) + sourceOffset.Y;

                float dy = y1 - y0;
                if (MathF.Abs(dy) < 1e-6f) continue;

                edges.Add(new Edge
                {
                    X0 = x0, Y0 = y0,
                    X1 = x1, Y1 = y1,
                    YMin = MathF.Min(y0, y1),
                    YMax = MathF.Max(y0, y1),
                    Direction = dy > 0 ? 1f : -1f,
                });
            }
        }
    }
}
