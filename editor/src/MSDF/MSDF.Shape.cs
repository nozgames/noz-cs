//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

partial class MSDF
{
    private class Shape
    {
        public Contour[] contours = null!;

        public bool InverseYAxis {
            get; set;
        } = false;

        private bool Validate()
        {
            foreach (var contour in contours)
            {
                if (contour.edges.Length == 0)
                    continue;

                Vector2Double corner = contour.edges.Last().GetPoint(1.0);
                foreach (var edge in contour.edges)
                {
                    var compare = edge.GetPoint(0.0);
                    if (!MathEx.Approximately(compare.x, corner.x) || !MathEx.Approximately(compare.y, corner.y))
                        return false;

                    corner = edge.GetPoint(1.0);
                }
            }

            return true;
        }

        private void Normalize()
        {
            foreach (var contour in contours)
            {
                if (contour.edges.Length != 1)
                    continue;

                contour.edges[0].SplitInThirds(out var part1, out var part2, out var part3);
                contour.edges = new Edge[3];
                contour.edges[0] = part1;
                contour.edges[1] = part2;
                contour.edges[2] = part3;
            }
        }

        public void Bounds(ref double l, ref double b, ref double r, ref double t)
        {
            foreach (var contour in contours)
            {
                contour.Bounds(ref l, ref b, ref r, ref t);
            }
        }

        /// <summary>
        /// Convert sprite editor paths into MSDF Shape for multi-channel distance field generation.
        /// Each non-subtract path becomes a contour. Subtract paths become contours with reversed winding.
        /// </summary>
        public static Shape FromSpritePaths(
            NoZ.Editor.Shape spriteShape,
            ReadOnlySpan<ushort> pathIndices,
            float pixelsPerUnit)
        {
            var contours = new List<Contour>();

            foreach (var pathIndex in pathIndices)
            {
                if (pathIndex >= spriteShape.PathCount) continue;
                ref readonly var path = ref spriteShape.GetPath(pathIndex);
                if (path.AnchorCount < 3) continue;

                var edges = new List<Edge>();

                for (ushort a = 0; a < path.AnchorCount; a++)
                {
                    var a0Idx = (ushort)(path.AnchorStart + a);
                    var a1Idx = (ushort)(path.AnchorStart + ((a + 1) % path.AnchorCount));

                    ref readonly var anchor0 = ref spriteShape.GetAnchor(a0Idx);
                    ref readonly var anchor1 = ref spriteShape.GetAnchor(a1Idx);

                    // Positions are in shape-space (not pixel-space)
                    var p0 = new Vector2Double(anchor0.Position.X, anchor0.Position.Y);
                    var p1 = new Vector2Double(anchor1.Position.X, anchor1.Position.Y);

                    if (Math.Abs(anchor0.Curve) < 0.0001)
                    {
                        // Linear segment
                        edges.Add(new LinearEdge(p0, p1));
                    }
                    else
                    {
                        // Quadratic curve: compute control point from curve value
                        // cp = midpoint(p0,p1) + perpendicular * curve
                        var mid = 0.5 * (p0 + p1);
                        var dir = p1 - p0;
                        var perpMag = dir.Magnitude;
                        Vector2Double perp;
                        if (perpMag > 1e-10)
                        {
                            perp = new Vector2Double(-dir.y, dir.x) * (1.0 / perpMag);
                        }
                        else
                        {
                            perp = new Vector2Double(0, 1);
                        }
                        var cp = mid + perp * anchor0.Curve;
                        edges.Add(new QuadraticEdge(p0, cp, p1));
                    }
                }

                if (edges.Count > 0)
                    contours.Add(new Contour { edges = edges.ToArray() });
            }

            var shape = new Shape { contours = contours.ToArray(), InverseYAxis = false };
            shape.Normalize();
            return shape;
        }

        public static Shape? FromGlyph(TrueTypeFont.Glyph glyph, bool invertYAxis)
        {
            if (null == glyph)
                return null;

            var shape = new Shape() { contours = new Contour[glyph.contours.Length] };

            for (int i = 0; i < glyph.contours.Length; i++)
            {
                ref var glyphContour = ref glyph.contours[i];

                // Capture contour values to avoid ref local issue in lambdas
                int contourStart = glyphContour.start;
                int contourLength = glyphContour.length;

                if (contourLength == 0)
                {
                    shape.contours[i] = new Contour { edges = Array.Empty<Edge>() };
                    continue;
                }

                List<Edge> edges = new List<Edge>();

                // Helper to get point at index (with wrapping)
                TrueTypeFont.Point GetPoint(int index) =>
                    glyph.points[contourStart + (index % contourLength)];

                // Helper to compute midpoint
                static Vector2Double Midpoint(Vector2Double a, Vector2Double b) =>
                    new Vector2Double((a.x + b.x) / 2, (a.y + b.y) / 2);

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
                    // Start from the first on-curve point
                    start = GetPoint(firstOnCurve).xy;
                    startIndex = firstOnCurve;
                }
                else
                {
                    // All points are conic - implicit on-curve at midpoint of first two
                    start = Midpoint(GetPoint(0).xy, GetPoint(1).xy);
                    startIndex = 0;
                }

                Vector2Double last = start;

                // Process all points starting from startIndex, wrapping around
                int processed = 0;
                int p_idx = startIndex;

                while (processed < contourLength)
                {
                    // Move to next point
                    p_idx = (p_idx + 1) % contourLength;
                    processed++;

                    var point = GetPoint(p_idx);

                    if (point.curve == TrueTypeFont.CurveType.Conic)
                    {
                        // Start of quadratic curve - collect consecutive conic points
                        var control = point.xy;

                        while (processed < contourLength)
                        {
                            int nextIdx = (p_idx + 1) % contourLength;
                            var nextPoint = GetPoint(nextIdx);

                            if (nextPoint.curve != TrueTypeFont.CurveType.Conic)
                            {
                                // End conic sequence with on-curve point
                                edges.Add(new QuadraticEdge(last, control, nextPoint.xy));
                                last = nextPoint.xy;
                                p_idx = nextIdx;
                                processed++;
                                break;
                            }
                            else
                            {
                                // Two consecutive conics - implicit on-curve at midpoint
                                var mid = Midpoint(control, nextPoint.xy);
                                edges.Add(new QuadraticEdge(last, control, mid));
                                last = mid;
                                control = nextPoint.xy;
                                p_idx = nextIdx;
                                processed++;
                            }
                        }

                        // If we consumed all points while in conic mode, close to start
                        if (processed >= contourLength && last != start)
                        {
                            // The last control point needs to connect back to start
                            edges.Add(new QuadraticEdge(last, control, start));
                        }
                    }
                    else
                    {
                        // On-curve point - create linear edge
                        edges.Add(new LinearEdge(last, point.xy));
                        last = point.xy;
                    }
                }

                // Close the contour if needed (last point was on-curve but not at start)
                if (edges.Count > 0 && last != start)
                {
                    var lastEdgeEnd = edges.Last().GetPoint(1.0);
                    if (!MathEx.Approximately(lastEdgeEnd.x, start.x) || !MathEx.Approximately(lastEdgeEnd.y, start.y))
                    {
                        edges.Add(new LinearEdge(last, start));
                    }
                }

                shape.contours[i] = new Contour { edges = edges.ToArray() };
            }

            if (!shape.Validate())
                throw new ImportException("Invalid shape data in glyph");

            shape.Normalize();
            shape.InverseYAxis = invertYAxis;

            return shape;
        }
    }
}
