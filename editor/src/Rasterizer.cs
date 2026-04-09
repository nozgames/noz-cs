//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  CPU 8x MSAA polygon rasterizer.
//
//  For each pixel, 8 sub-samples are tested independently against each path
//  using a non-zero winding rule. Paths are blended back-to-front into the
//  per-sample accumulator with Porter-Duff over, then averaged in Resolve().
//  Sub-sample positions follow the D3D11 8x rotated grid (one sample per
//  sub-row), so each pixel row is rasterized via 8 sub-scanlines walking an
//  active edge table.
//
//  This avoids the shared-edge bleed of analytic per-path coverage: at a
//  pixel straddling an edge shared by two paths, sub-samples covered by
//  both receive the topmost path's color via opaque overwrite, and samples
//  covered by neither stay transparent.
//

using System.Runtime.CompilerServices;
using Clipper2Lib;

namespace NoZ.Editor;

[InlineArray(8)]
internal struct Sample8
{
    private Color32 _element0;
}

internal static class Rasterizer
{
    [ThreadStatic] private static List<Edge>? _edgePool;
    [ThreadStatic] private static List<int>? _aetPool;
    [ThreadStatic] private static float[]? _xsPool;
    [ThreadStatic] private static int[]? _wsPool;

    private static readonly Comparison<Edge> EdgeYMinComparison = (a, b) => a.YMin.CompareTo(b.YMin);

    // D3D11 8x MSAA sample positions, converted from signed 16ths via (v + 8) / 16:
    //   (1,-3), (-1,3), (5,1), (-3,-5), (-5,5), (-7,-1), (3,7), (7,-7)
    // Each sample sits on a distinct sub-row (Y values are all unique), which
    // lets the rasterizer run one scanline per sub-row.
    private static readonly (float X, float Y)[] SampleOffsets =
    {
        (0.5625f, 0.3125f), (0.4375f, 0.6875f),
        (0.8125f, 0.5625f), (0.3125f, 0.1875f),
        (0.1875f, 0.8125f), (0.0625f, 0.4375f),
        (0.6875f, 0.9375f), (0.9375f, 0.0625f),
    };

    public static void Fill(
        PathsD paths,
        PixelData<Sample8> samples,
        RectInt targetRect,
        Vector2Int sourceOffset,
        int dpi,
        Color32 color)
    {
        int w = targetRect.Width;
        int h = targetRect.Height;
        if (w <= 0 || h <= 0 || paths.Count == 0) return;

        var edges = _edgePool ??= new List<Edge>();
        edges.Clear();
        CollectEdges(edges, paths, dpi, sourceOffset);
        if (edges.Count == 0) return;

        edges.Sort(EdgeYMinComparison);

        var aet = _aetPool ??= new List<int>();
        aet.Clear();

        var xs = _xsPool;
        var ws = _wsPool;
        if (xs == null || ws == null)
        {
            xs = new float[64];
            ws = new int[64];
            _xsPool = xs;
            _wsPool = ws;
        }

        int nextEdge = 0;
        int edgeCount = edges.Count;
        bool opaque = color.A == 255;

        for (int py = 0; py < h; py++)
        {
            // Compact AET in place: drop edges whose YMax has passed this row.
            int writeIdx = 0;
            for (int readIdx = 0; readIdx < aet.Count; readIdx++)
            {
                int ei = aet[readIdx];
                if (edges[ei].YMax > py)
                {
                    if (writeIdx != readIdx) aet[writeIdx] = ei;
                    writeIdx++;
                }
            }
            if (writeIdx < aet.Count)
                aet.RemoveRange(writeIdx, aet.Count - writeIdx);

            // Admit edges whose YMin falls inside this pixel row.
            float rowBot = py + 1;
            while (nextEdge < edgeCount && edges[nextEdge].YMin < rowBot)
            {
                if (edges[nextEdge].YMax > py)
                    aet.Add(nextEdge);
                nextEdge++;
            }

            if (aet.Count == 0) continue;

            for (int s = 0; s < 8; s++)
            {
                float subY = py + SampleOffsets[s].Y;

                // Collect x-crossings of the AET against this sub-scanline.
                int n = 0;
                for (int k = 0; k < aet.Count; k++)
                {
                    var e = edges[aet[k]];
                    // Half-open vertical range [YMin, YMax) — keeps edges that
                    // touch a sub-row Y exactly from being double-counted.
                    if (subY < e.YMin || subY >= e.YMax) continue;

                    float t = (subY - e.Y0) / (e.Y1 - e.Y0);
                    float x = e.X0 + t * (e.X1 - e.X0);

                    if (n >= xs.Length)
                    {
                        var newXs = new float[xs.Length * 2];
                        var newWs = new int[ws.Length * 2];
                        Array.Copy(xs, newXs, xs.Length);
                        Array.Copy(ws, newWs, ws.Length);
                        xs = newXs;
                        ws = newWs;
                        _xsPool = xs;
                        _wsPool = ws;
                    }
                    xs[n] = x;
                    ws[n] = (int)e.Direction;
                    n++;
                }

                if (n < 2) continue;

                // Insertion sort by x — n is typically small (< 20).
                for (int i = 1; i < n; i++)
                {
                    float kx = xs[i];
                    int kw = ws[i];
                    int j = i - 1;
                    while (j >= 0 && xs[j] > kx)
                    {
                        xs[j + 1] = xs[j];
                        ws[j + 1] = ws[j];
                        j--;
                    }
                    xs[j + 1] = kx;
                    ws[j + 1] = kw;
                }

                // Walk pixel columns, integrating winding through the crossings.
                int ci = 0;
                int winding = 0;
                float subX = SampleOffsets[s].X;
                for (int px = 0; px < w; px++)
                {
                    float sampleX = px + subX;
                    while (ci < n && xs[ci] <= sampleX)
                    {
                        winding += ws[ci];
                        ci++;
                    }
                    if (winding != 0)
                    {
                        ref var sample = ref samples[px, py];
                        // Overwrite if the slot is still transparent or the
                        // incoming color is opaque — avoids the dark halo
                        // Color32.Blend produces when lerping RGB against a
                        // (0,0,0,0) dst.
                        if (opaque || sample[s].A == 0)
                            sample[s] = color;
                        else
                            sample[s] = Color32.Blend(sample[s], color);
                    }
                }
            }
        }
    }

    public static void Resolve(
        PixelData<Color32> target,
        PixelData<Sample8> samples,
        RectInt targetRect)
    {
        int w = targetRect.Width;
        int h = targetRect.Height;
        for (int py = 0; py < h; py++)
        {
            int ty = targetRect.Y + py;
            for (int px = 0; px < w; px++)
            {
                ref var row = ref samples[px, py];
                int sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                for (int s = 0; s < 8; s++)
                {
                    var c = row[s];
                    // Weight RGB by per-sample alpha so transparent slots
                    // don't drag the color toward black. This produces a
                    // straight-alpha result: RGB is the color of covered
                    // samples, alpha is the coverage fraction.
                    sumR += c.R * c.A;
                    sumG += c.G * c.A;
                    sumB += c.B * c.A;
                    sumA += c.A;
                }
                if (sumA == 0) continue;

                int halfA = sumA >> 1;
                var avg = new Color32(
                    (byte)((sumR + halfA) / sumA),
                    (byte)((sumG + halfA) / sumA),
                    (byte)((sumB + halfA) / sumA),
                    (byte)((sumA + 4) >> 3));

                int tx = targetRect.X + px;
                ref var dst = ref target[tx, ty];
                dst = dst.A == 0 ? avg : Color32.Blend(dst, avg);
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
