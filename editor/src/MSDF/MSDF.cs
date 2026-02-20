//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoZ.Editor
{
    partial class MSDF
    {
        public static void RenderGlyph(
            TrueTypeFont.Glyph glyph,
            PixelData<byte> output,
            Vector2Int outputPosition,
            Vector2Int outputSize,
            double range,
            in Vector2Double scale,
            in Vector2Double translate
            )
        {
            GenerateSDF(
                output,
                outputPosition,
                outputSize,
                Shape.FromGlyph(glyph, true),
                range,
                scale,
                translate
            );
        }

        // Compute winding number contribution from a contour using analytical ray casting.
        // Quadratic edges are decomposed into y-monotone segments so that the same
        // half-open y-interval rule used for linear edges applies consistently.
        private static int ComputeWindingNumber(Contour contour, Vector2Double p)
        {
            int winding = 0;

            foreach (var edge in contour.edges)
            {
                if (edge is LinearEdge line)
                {
                    double y0 = line.p0.y, y1 = line.p1.y;
                    if (y0 <= p.y)
                    {
                        if (y1 > p.y)
                        {
                            double t = (p.y - y0) / (y1 - y0);
                            if (line.p0.x + t * (line.p1.x - line.p0.x) > p.x)
                                winding++;
                        }
                    }
                    else if (y1 <= p.y)
                    {
                        double t = (p.y - y0) / (y1 - y0);
                        if (line.p0.x + t * (line.p1.x - line.p0.x) > p.x)
                            winding--;
                    }
                }
                else if (edge is QuadraticEdge q)
                {
                    // Decompose into y-monotone segments by splitting at the y-extremum.
                    // This ensures consistent half-open interval handling with linear edges.
                    double ay = q.p0.y - 2 * q.p1.y + q.p2.y;

                    if (Math.Abs(ay) > 1e-12)
                    {
                        double tExt = (q.p0.y - q.p1.y) / ay;
                        if (tExt > 0 && tExt < 1)
                        {
                            // Non-monotone: split at y-extremum and process two pieces
                            double yExt = QuadY(q, tExt);
                            QuadMonoCrossing(q, p, 0, tExt, q.p0.y, yExt, ref winding);
                            QuadMonoCrossing(q, p, tExt, 1, yExt, q.p2.y, ref winding);
                            continue;
                        }
                    }

                    // Already y-monotone
                    QuadMonoCrossing(q, p, 0, 1, q.p0.y, q.p2.y, ref winding);
                }
            }

            return winding;
        }

        private static double QuadY(QuadraticEdge q, double t)
        {
            double omt = 1 - t;
            return omt * omt * q.p0.y + 2 * omt * t * q.p1.y + t * t * q.p2.y;
        }

        // Check for a ray crossing within a y-monotone quadratic segment [tA, tB].
        // Uses the same half-open interval rule as linear edges (include lower y, exclude upper y).
        private static void QuadMonoCrossing(
            QuadraticEdge q, Vector2Double p,
            double tA, double tB, double yA, double yB,
            ref int winding)
        {
            int dir;
            if (yA <= p.y && yB > p.y)
                dir = 1;  // upward
            else if (yA > p.y && yB <= p.y)
                dir = -1; // downward
            else
                return;

            // Solve (y0 - 2*y1 + y2)*t^2 + 2*(y1-y0)*t + (y0-py) = 0
            double a = q.p0.y - 2 * q.p1.y + q.p2.y;
            double b = 2 * (q.p1.y - q.p0.y);
            double c = q.p0.y - p.y;

            double t;
            if (Math.Abs(a) < 1e-12)
            {
                t = (Math.Abs(b) > 1e-12) ? -c / b : (tA + tB) * 0.5;
            }
            else
            {
                double disc = b * b - 4 * a * c;
                if (disc < 0) disc = 0;
                double sqrtDisc = Math.Sqrt(disc);
                double inv2a = 0.5 / a;
                double t1 = (-b + sqrtDisc) * inv2a;
                double t2 = (-b - sqrtDisc) * inv2a;

                // Pick the root that falls within [tA, tB]
                t = (t1 >= tA - 1e-6 && t1 <= tB + 1e-6) ? t1 : t2;
                t = Math.Clamp(t, tA, tB);
            }

            double omt = 1 - t;
            double xAt = omt * omt * q.p0.x + 2 * omt * t * q.p1.x + t * t * q.p2.x;
            if (xAt > p.x)
                winding += dir;
        }

        /// <summary>
        /// Test whether the junction between two edges constitutes a sharp corner.
        /// Matches msdfgen's isCorner: dot <= 0 (orthogonal+) OR |cross| > threshold.
        /// </summary>
        private static bool IsCorner(Vector2Double aDir, Vector2Double bDir, double crossThreshold)
        {
            return Vector2Double.Dot(aDir, bDir) <= 0
                || Math.Abs(Vector2Double.Cross(aDir, bDir)) > crossThreshold;
        }

        /// <summary>
        /// Assign R/G/B edge colors to a contour's edges so that edges meeting
        /// at sharp corners get different channel assignments.
        /// Matches msdfgen's edgeColoringSimple algorithm.
        /// </summary>
        private static void ColorContourEdges(Contour contour, double angleThreshold = 3.0)
        {
            var edges = contour.edges;
            if (edges.Length == 0)
                return;

            // Single edge: assign cyan (G+B) so it spans two channels
            if (edges.Length == 1)
            {
                edges[0].color = EdgeColor.Cyan;
                return;
            }

            // Two edges: assign magenta and yellow (each spans 2 channels, sharing Green)
            if (edges.Length == 2)
            {
                edges[0].color = EdgeColor.Magenta;
                edges[1].color = EdgeColor.Yellow;
                return;
            }

            // Convert angle threshold to cross-product threshold (matches msdfgen)
            double crossThreshold = Math.Sin(angleThreshold);

            // Detect sharp corners using msdfgen's dual test
            var corners = new List<int>();
            var prevDirection = GetEdgeDirection(edges[^1], 1.0);
            for (int i = 0; i < edges.Length; i++)
            {
                var curDirection = GetEdgeDirection(edges[i], 0.0);
                if (IsCorner(prevDirection, curDirection, crossThreshold))
                    corners.Add(i);
                prevDirection = GetEdgeDirection(edges[i], 1.0);
            }

            // If no sharp corners, assign a single color to the whole contour
            if (corners.Count == 0)
            {
                EdgeColor color = EdgeColor.Cyan;
                for (int i = 0; i < edges.Length; i++)
                    edges[i].color = color;
                return;
            }

            // Assign colors: at each sharp corner, switch color.
            // Matches msdfgen's edgeColoringSimple multi-corner case.
            EdgeColor[] colorCycle = [EdgeColor.Cyan, EdgeColor.Magenta, EdgeColor.Yellow];
            int colorIndex = 0;
            int start = corners[0];
            int spline = 0;
            int m = edges.Length;

            for (int i = 0; i < m; i++)
            {
                int index = (start + i) % m;
                if (spline + 1 < corners.Count && corners[spline + 1] == index)
                {
                    spline++;
                    colorIndex++;
                }
                edges[index].color = colorCycle[colorIndex % colorCycle.Length];
            }

            // Fix wrap-around: if the last edge and first edge share a color
            // at the closing corner, reassign the last edge. msdfgen handles
            // this via switchColor(color, seed, banned=initialColor).
            if (corners.Count >= 2)
            {
                var firstColor = edges[corners[0]].color;
                int lastEdgeIdx = (corners[0] + m - 1) % m;
                var lastColor = edges[lastEdgeIdx].color;
                if (firstColor == lastColor)
                {
                    int prevEdgeIdx = (corners[0] + m - 2) % m;
                    var prevColor = edges[prevEdgeIdx].color;
                    foreach (var c in colorCycle)
                    {
                        if (c != firstColor && c != prevColor)
                        {
                            edges[lastEdgeIdx].color = c;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Get the tangent direction of an edge at parameter t (normalized).
        /// </summary>
        private static Vector2Double GetEdgeDirection(Edge edge, double t)
        {
            Vector2Double dir;
            if (edge is LinearEdge line)
            {
                dir = line.p1 - line.p0;
            }
            else if (edge is QuadraticEdge q)
            {
                // Derivative of quadratic: 2(1-t)(p1-p0) + 2t(p2-p1)
                dir = 2.0 * (1.0 - t) * (q.p1 - q.p0) + 2.0 * t * (q.p2 - q.p1);
            }
            else
            {
                dir = new Vector2Double(1, 0);
            }

            double mag = dir.Magnitude;
            return mag > 1e-12 ? dir * (1.0 / mag) : new Vector2Double(1, 0);
        }

        /// <summary>
        /// Apply edge coloring to all contours in a shape.
        /// </summary>
        private static void ColorEdges(Shape shape, double angleThreshold = 3.0)
        {
            foreach (var contour in shape.contours)
                ColorContourEdges(contour, angleThreshold);
        }

        /// <summary>
        /// Pre-computed MSDF data for a single sprite path: edges grouped by channel.
        /// Opaque to callers — use GetChannelSignedDistances to query.
        /// </summary>
        internal struct PathMSDF
        {
            private Edge[] _redEdges;
            private Edge[] _greenEdges;
            private Edge[] _blueEdges;

            internal bool IsValid => _redEdges != null;

            internal PathMSDF(Edge[] red, Edge[] green, Edge[] blue)
            {
                _redEdges = red;
                _greenEdges = green;
                _blueEdges = blue;
            }

            /// <summary>
            /// Compute the nearest signed distance per channel. Each channel
            /// independently finds its closest edge and preserves the sign from
            /// that edge's signed distance. At sharp corners, different channels
            /// see different nearest edges with potentially different signs —
            /// this is what creates sharp features in the median reconstruction.
            /// </summary>
            internal (double r, double g, double b) GetChannelSignedDistances(Vector2Double p)
            {
                return (
                    GetNearestSignedDistance(_redEdges, p),
                    GetNearestSignedDistance(_greenEdges, p),
                    GetNearestSignedDistance(_blueEdges, p)
                );
            }
        }

        /// <summary>
        /// Build MSDF data for a single sprite path: convert anchors to edges,
        /// apply edge coloring, and group by channel.
        /// </summary>
        internal static PathMSDF BuildPathMSDF(NoZ.Editor.Shape spriteShape, ushort pathIndex)
        {
            ref readonly var path = ref spriteShape.GetPath(pathIndex);

            var edges = new List<Edge>();

            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var a0Idx = (ushort)(path.AnchorStart + a);
                var a1Idx = (ushort)(path.AnchorStart + ((a + 1) % path.AnchorCount));

                ref readonly var anchor0 = ref spriteShape.GetAnchor(a0Idx);
                ref readonly var anchor1 = ref spriteShape.GetAnchor(a1Idx);

                var p0 = new Vector2Double(anchor0.Position.X, anchor0.Position.Y);
                var p1 = new Vector2Double(anchor1.Position.X, anchor1.Position.Y);

                if (Math.Abs(anchor0.Curve) < 0.0001)
                {
                    edges.Add(new LinearEdge(p0, p1));
                }
                else
                {
                    var mid = 0.5 * (p0 + p1);
                    var dir = p1 - p0;
                    var perpMag = dir.Magnitude;
                    Vector2Double perp;
                    if (perpMag > 1e-10)
                        perp = new Vector2Double(-dir.y, dir.x) * (1.0 / perpMag);
                    else
                        perp = new Vector2Double(0, 1);
                    var cp = mid + perp * anchor0.Curve;
                    edges.Add(new QuadraticEdge(p0, cp, p1));
                }
            }

            // Build contour and apply edge coloring
            var contour = new Contour { edges = edges.ToArray() };
            if (contour.edges.Length == 1)
            {
                contour.edges[0].SplitInThirds(out var p1, out var p2, out var p3);
                contour.edges = [p1, p2, p3];
            }
            ColorContourEdges(contour);

            // Group edges by channel
            var red = new List<Edge>();
            var green = new List<Edge>();
            var blue = new List<Edge>();
            foreach (var edge in contour.edges)
            {
                if ((edge.color & EdgeColor.Red) != 0) red.Add(edge);
                if ((edge.color & EdgeColor.Green) != 0) green.Add(edge);
                if ((edge.color & EdgeColor.Blue) != 0) blue.Add(edge);
            }

            return new PathMSDF(red.ToArray(), green.ToArray(), blue.ToArray());
        }

        /// <summary>
        /// Find the nearest edge in the array and return its signed distance.
        /// The SignedDistance comparison (by absolute distance) finds the closest
        /// edge, but we return the actual signed distance value which preserves
        /// inside/outside information per channel.
        /// </summary>
        private static double GetNearestSignedDistance(Edge[] edges, Vector2Double p)
        {
            SignedDistance minSD = SignedDistance.Infinite;
            foreach (var edge in edges)
            {
                var sd = edge.GetSignedDistance(p, out _);
                if (sd < minSD)
                    minSD = sd;
            }
            return minSD.distance;
        }

        // Compute the total winding number across all contours at point p.
        private static int ComputeTotalWinding(Shape shape, Vector2Double p)
        {
            int total = 0;
            foreach (var contour in shape.contours)
                total += ComputeWindingNumber(contour, p);
            return total;
        }

        private static void GenerateSDF(
            PixelData<byte> output,
            Vector2Int outputPosition,
            Vector2Int outputSize,
            Shape? shape,
            double range,
            Vector2Double scale,
            Vector2Double translate)
        {
            if (shape == null)
                return;

            int w = outputSize.X;
            int h = outputSize.Y;

            // Collect all edges from all contours into a flat list for distance queries.
            int totalEdges = 0;
            foreach (var contour in shape.contours)
                totalEdges += contour.edges.Length;
            var allEdges = new Edge[totalEdges];
            int ei = 0;
            foreach (var contour in shape.contours)
                foreach (var edge in contour.edges)
                    allEdges[ei++] = edge;

            Parallel.For(0, h, y =>
            {
                int row = shape.InverseYAxis ? h - y - 1 : y;

                for (int x = 0; x < w; ++x)
                {
                    double dummy = 0;
                    Vector2Double p = new Vector2Double(x + .5, y + .5) / scale - translate;

                    // Use winding number for inside/outside (handles all overlaps correctly)
                    bool inside = ComputeTotalWinding(shape, p) != 0;

                    // Find the minimum absolute distance to any edge
                    double minAbsDist = double.PositiveInfinity;
                    foreach (var edge in allEdges)
                    {
                        double absDist = Math.Abs(edge.GetSignedDistance(p, out dummy).distance);
                        if (absDist < minAbsDist)
                            minAbsDist = absDist;
                    }

                    // For inside pixels, check if the nearest edge is an internal overlap edge
                    // by testing the winding at a point nudged toward the nearest edge. If moving
                    // toward the nearest edge doesn't eventually cross outside, the edge is internal
                    // and we should use the distance to the next-closest boundary edge instead.
                    if (inside && minAbsDist < range)
                    {
                        // Sample a point just past the nearest edge (1 pixel beyond)
                        // If it's still inside, the nearest edge is internal — find the real boundary
                        double probeDistance = minAbsDist + 1.0;
                        double bestBoundaryDist = double.PositiveInfinity;

                        foreach (var edge in allEdges)
                        {
                            var sd = edge.GetSignedDistance(p, out double param);
                            double absDist = Math.Abs(sd.distance);

                            // Get the direction to this edge
                            double clampedParam = Math.Clamp(param, 0, 1);
                            Vector2Double edgePoint = edge.GetPoint(clampedParam);
                            Vector2Double toEdge = edgePoint - p;
                            double toEdgeMag = toEdge.Magnitude;

                            if (toEdgeMag < 1e-10)
                            {
                                // We're on the edge — this is a real boundary
                                bestBoundaryDist = 0;
                                break;
                            }

                            // Probe a point slightly past this edge
                            Vector2Double probeDir = toEdge * (1.0 / toEdgeMag);
                            Vector2Double probe = p + probeDir * (toEdgeMag + 0.5);
                            bool probeInside = ComputeTotalWinding(shape, probe) != 0;

                            // If the probe is outside, this edge is a real boundary
                            if (!probeInside && absDist < bestBoundaryDist)
                                bestBoundaryDist = absDist;
                        }

                        if (bestBoundaryDist < double.PositiveInfinity)
                            minAbsDist = bestBoundaryDist;
                    }

                    double finalSD = inside ? minAbsDist : -minAbsDist;

                    // Set the SDF value in the output image (R8 format)
                    finalSD /= (range * 2.0f);
                    finalSD = Math.Clamp(finalSD, -0.5, 0.5) + 0.5;

                    output.Set(
                        x + outputPosition.X,
                        row + outputPosition.Y,
                        (byte)(finalSD * 255.0f));
                }
            });
        }
    }
}
