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

using Clipper2Lib;

namespace NoZ.Editor;

internal struct EdgePixel
{
    public Color32 Color;
    public byte Coverage;
}

internal static class Rasterizer
{
    [ThreadStatic] private static List<Edge>? _edgePool;
    [ThreadStatic] private static float[]? _coveragePool;

    private static readonly Comparison<Edge> EdgeYMinComparison = (a, b) => a.YMin.CompareTo(b.YMin);

    public static void Fill(
        PathsD paths,
        PixelData<Color32> target,
        PixelData<EdgePixel> edgeBuffer,
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

                if (alpha >= 0.5f)
                {
                    // Binary interior write to primary buffer.
                    int tx = targetRect.X + px;
                    var dst = target[tx, ty];
                    if (dst.A == 0 || color.A == 255)
                    {
                        // Empty destination or opaque path — straight overwrite.
                        target[tx, ty] = color;
                    }
                    else
                    {
                        // Semi-transparent path — Porter-Duff over with srcA = color.A.
                        target[tx, ty] = Color32.Blend(dst, color);
                    }

                    // An interior pixel hides any prior edge contribution here.
                    edgeBuffer[px, py] = default;
                }
                else if (alpha > 0.004f) // ~1/255
                {
                    // Edge (fractional coverage) — record in edge buffer (last-writer-wins).
                    // Remap (0, 0.5) -> (0, 1) by multiplying by 2, then modulate by path alpha.
                    float cov = alpha * 2f * (color.A / 255f);
                    if (cov > 1f) cov = 1f;
                    edgeBuffer[px, py] = new EdgePixel
                    {
                        Color = color,
                        Coverage = (byte)(cov * 255f + 0.5f),
                    };
                }
            }
        }
    }

    public static void Composite(
        PixelData<Color32> target,
        PixelData<EdgePixel> edgeBuffer,
        RectInt targetRect)
    {
        int w = targetRect.Width;
        int h = targetRect.Height;
        for (int py = 0; py < h; py++)
        {
            int ty = targetRect.Y + py;
            for (int px = 0; px < w; px++)
            {
                ref var ep = ref edgeBuffer[px, py];
                if (ep.Coverage == 0) continue;

                int tx = targetRect.X + px;
                ref var dst = ref target[tx, ty];
                // Integer-weighted lerp: dst = lerp(dst, ep.Color, ep.Coverage / 255)
                int t = ep.Coverage;
                int it = 255 - t;
                dst = new Color32(
                    (byte)((dst.R * it + ep.Color.R * t) / 255),
                    (byte)((dst.G * it + ep.Color.G * t) / 255),
                    (byte)((dst.B * it + ep.Color.B * t) / 255),
                    (byte)((dst.A * it + ep.Color.A * t) / 255));
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
