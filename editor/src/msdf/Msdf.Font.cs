//
//  Bridge between TTF font glyphs and MSDF generation.
//

using System;

namespace NoZ.Editor.Msdf;

internal static class MsdfFont
{
    /// <summary>
    /// Convert a TTF glyph into an msdf Shape with edge coloring applied,
    /// ready for MSDF generation.
    /// </summary>
    public static Shape? FromGlyph(TrueTypeFont.Glyph glyph, bool inverseYAxis)
    {
        if (glyph?.contours == null || glyph.contours.Length == 0)
            return null;

        var shape = new Shape { inverseYAxis = inverseYAxis };

        for (int ci = 0; ci < glyph.contours.Length; ci++)
        {
            ref var glyphContour = ref glyph.contours[ci];
            int contourStart = glyphContour.start;
            int contourLength = glyphContour.length;

            if (contourLength == 0)
                continue;

            var contour = shape.AddContour();

            TrueTypeFont.Point GetPoint(int index) =>
                glyph.points[contourStart + (index % contourLength)];

            static Vector2Double Midpoint(Vector2Double a, Vector2Double b) =>
                new((a.x + b.x) / 2, (a.y + b.y) / 2);

            // Find the first on-curve point, or compute implicit start if all are conic
            int firstOnCurve = -1;
            for (int p = 0; p < contourLength; p++)
            {
                if (GetPoint(p).curve != TrueTypeFont.CurveType.Conic)
                {
                    firstOnCurve = p;
                    break;
                }
            }

            Vector2Double start;
            int startIndex;

            if (firstOnCurve >= 0)
            {
                start = GetPoint(firstOnCurve).xy;
                startIndex = firstOnCurve;
            }
            else
            {
                start = Midpoint(GetPoint(0).xy, GetPoint(1).xy);
                startIndex = 0;
            }

            Vector2Double last = start;
            int processed = 0;
            int pIdx = startIndex;

            while (processed < contourLength)
            {
                pIdx = (pIdx + 1) % contourLength;
                processed++;

                var point = GetPoint(pIdx);

                if (point.curve == TrueTypeFont.CurveType.Conic)
                {
                    var control = point.xy;

                    while (processed < contourLength)
                    {
                        int nextIdx = (pIdx + 1) % contourLength;
                        var nextPoint = GetPoint(nextIdx);

                        if (nextPoint.curve != TrueTypeFont.CurveType.Conic)
                        {
                            contour.AddEdge(new QuadraticSegment(last, control, nextPoint.xy));
                            last = nextPoint.xy;
                            pIdx = nextIdx;
                            processed++;
                            break;
                        }
                        else
                        {
                            var mid = Midpoint(control, nextPoint.xy);
                            contour.AddEdge(new QuadraticSegment(last, control, mid));
                            last = mid;
                            control = nextPoint.xy;
                            pIdx = nextIdx;
                            processed++;
                        }
                    }

                    if (processed >= contourLength && last != start)
                        contour.AddEdge(new QuadraticSegment(last, control, start));
                }
                else
                {
                    contour.AddEdge(new LinearSegment(last, point.xy));
                    last = point.xy;
                }
            }

            // Close the contour if needed
            if (contour.edges.Count > 0 && last != start)
            {
                var lastEdgeEnd = contour.edges[^1].Point(1.0);
                if (!MathEx.Approximately(lastEdgeEnd.x, start.x) || !MathEx.Approximately(lastEdgeEnd.y, start.y))
                    contour.AddEdge(new LinearSegment(last, start));
            }
        }

        shape.Normalize();
        shape.OrientContours();
        EdgeColoring.ColorSimple(shape, 3.0);

        return shape;
    }

    /// <summary>
    /// Render a glyph as MSDF into a 3-channel float bitmap.
    /// </summary>
    public static void RenderGlyph(
        TrueTypeFont.Glyph glyph,
        MsdfBitmap output,
        Vector2Int outputPosition,
        Vector2Int outputSize,
        double range,
        in Vector2Double scale,
        in Vector2Double translate)
    {
        var shape = FromGlyph(glyph, true);
        if (shape == null)
            return;

        // Create a sub-bitmap view for the glyph region
        var glyphBitmap = new MsdfBitmap(outputSize.X, outputSize.Y);
        MsdfGenerator.GenerateMSDF(glyphBitmap, shape, range * 2.0, scale, translate);

        // Copy to output at the correct position
        for (int y = 0; y < outputSize.Y; y++)
        {
            for (int x = 0; x < outputSize.X; x++)
            {
                var src = glyphBitmap[x, y];
                var dst = output[x + outputPosition.X, y + outputPosition.Y];
                dst[0] = src[0];
                dst[1] = src[1];
                dst[2] = src[2];
            }
        }
    }
}
