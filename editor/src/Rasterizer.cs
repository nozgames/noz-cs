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

internal static class Rasterizer
{
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

        // Collect all edges in pixel space
        var edges = new List<Edge>();
        CollectEdges(edges, paths, dpi, sourceOffset);
        if (edges.Count == 0) return;

        // Sort edges by their minimum Y for efficient scanline processing
        edges.Sort((a, b) => a.YMin.CompareTo(b.YMin));

        // Coverage accumulator — one float per pixel column.
        // Each entry accumulates signed area for that column within one scanline.
        var coverage = new float[w + 2];

        int edgeStart = 0;

        for (int py = 0; py < h; py++)
        {
            Array.Clear(coverage, 0, w + 2);

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

                float dy = clipBot - clipTop; // fraction of scanline covered
                float dir = edge.Direction;

                // Deposit signed area into coverage buffer.
                // For a vertical-ish edge within one scanline:
                // The edge sweeps from xAtTop to xAtBot over height dy.
                // At each pixel column, we compute the area between the edge
                // and the right side of the pixel.
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
                    target[tx, ty] = (dst.A == 0)
                        ? srcColor
                        : Color32.Blend(dst, srcColor);
                }
            }
        }
    }

    // Deposit the signed area contribution of one edge segment into the coverage buffer.
    //
    // The edge goes from (x0, top) to (x1, bottom) within a single scanline row.
    // signedDy = (bottom - top) * direction, where direction is +1 (downward) or -1 (upward).
    //
    // For the running-sum approach, we need: after summing coverage[0..px],
    // the result = total signed area of all edges to the left of pixel px+1.
    // A pixel fully to the right of all edges has coverage = winding number.
    //
    // For an edge at position X within a scanline of height fraction h:
    //   - Pixels entirely left of X: contribute +h to winding (fully inside)
    //   - The pixel containing X: partial contribution based on X position within pixel
    //   - Pixels right of X: no contribution from this edge
    //
    // Using delta encoding: coverage[col] stores the change in the running sum at col.
    private static void DepositEdge(float[] coverage, int w, float x0, float x1, float signedDy)
    {
        // For near-vertical edges within one scanline, the X span is small.
        // We compute the average X position and treat it as a single crossing.
        // This is equivalent to the "area under the edge" calculation.

        float xLeft = MathF.Min(x0, x1);
        float xRight = MathF.Max(x0, x1);

        // If the edge spans less than ~1 pixel horizontally, treat as single crossing
        // at the midpoint. This is the common case for most edges.
        int iLeft = Math.Max((int)MathF.Floor(xLeft), 0);
        int iRight = Math.Min((int)MathF.Floor(xRight), w - 1);

        if (iLeft == iRight)
        {
            // Edge stays within one pixel column
            float xMid = (x0 + x1) * 0.5f;
            int ix = Math.Max((int)MathF.Floor(xMid), 0);
            ix = Math.Min(ix, w - 1);

            float frac = xMid - ix; // 0..1 position within pixel
            frac = MathF.Max(0, MathF.Min(1, frac));

            // Area to the right of the edge within this pixel = (1 - frac) * signedDy
            // Delta encoding: this pixel gets (1-frac)*h, next pixel gets frac*h... no.
            //
            // Think of it this way: the running sum after pixel ix should increase by
            // signedDy (the full winding contribution). But pixel ix is only partially
            // covered. The area of pixel ix that's "inside" (to the right of the edge)
            // is frac * signedDy. So:
            //   coverage[ix] += frac * signedDy  (partial coverage starts here)
            //   coverage[ix+1] += (1-frac) * signedDy  (remainder spills to next)
            // After running sum: pixels left of ix get 0, ix gets frac*h,
            // ix+1 and beyond get frac*h + (1-frac)*h = h. Correct!

            // Wait — which direction is "inside"? For a left-to-right running sum
            // with non-zero winding rule: an edge going downward (+dir) means
            // everything to its RIGHT is inside. The fraction of pixel ix that's
            // to the right of position xMid is (1 - frac). So pixel ix coverage = (1-frac)*h.
            // But we want the running sum to reach h for all pixels to the right.
            //
            // Actually, the standard formulation is simpler:
            // coverage[ix] += (1 - frac) * signedDy
            // coverage[ix+1] += frac * signedDy
            // Hmm, this is getting confusing. Let me just use the stb_truetype approach directly.
            //
            // stb_truetype: for an edge at x within pixel ix:
            //   The signed area delta at pixel ix = (ix + 1 - xMid) * signedDy
            //   This is the area from xMid to the right edge of pixel ix.
            //   Then coverage[ix+1] gets the rest: signedDy - that.
            //
            // This way, running sum gives:
            //   sum at ix = (ix+1 - xMid) * h  (partial pixel)
            //   sum at ix+1 = h  (full coverage for all pixels to the right)

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

                // Average X position of the edge within this pixel column
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
