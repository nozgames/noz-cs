//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System;
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
        // For each edge, computes exact ray-edge intersections instead of sampling.
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
                    // Solve (1-t)^2*y0 + 2t(1-t)*y1 + t^2*y2 = py for t
                    // Rearranges to: a*t^2 + b*t + c = 0
                    double a = q.p0.y - 2 * q.p1.y + q.p2.y;
                    double b = 2 * (q.p1.y - q.p0.y);
                    double c = q.p0.y - p.y;

                    double disc = b * b - 4 * a * c;
                    if (disc < 0) continue;

                    // Near-linear case (a ≈ 0)
                    if (Math.Abs(a) < 1e-14)
                    {
                        if (Math.Abs(b) < 1e-14) continue;
                        double t = -c / b;
                        if (t >= 0 && t < 1)
                        {
                            double omt = 1 - t;
                            double xAt = omt * omt * q.p0.x + 2 * omt * t * q.p1.x + t * t * q.p2.x;
                            if (xAt > p.x)
                            {
                                double dydt = 2 * omt * (q.p1.y - q.p0.y) + 2 * t * (q.p2.y - q.p1.y);
                                winding += dydt > 0 ? 1 : -1;
                            }
                        }
                        continue;
                    }

                    double sqrtDisc = Math.Sqrt(disc);
                    double inv2a = 1.0 / (2 * a);
                    for (int i = 0; i < 2; i++)
                    {
                        double t = (-b + (i == 0 ? sqrtDisc : -sqrtDisc)) * inv2a;
                        if (t >= 0 && t < 1)
                        {
                            double omt = 1 - t;
                            double xAt = omt * omt * q.p0.x + 2 * omt * t * q.p1.x + t * t * q.p2.x;
                            if (xAt > p.x)
                            {
                                double dydt = 2 * omt * (q.p1.y - q.p0.y) + 2 * t * (q.p2.y - q.p1.y);
                                winding += dydt > 0 ? 1 : -1;
                            }
                        }
                    }
                }
            }

            return winding;
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

            int contourCount = shape.contours.Length;
            int w = outputSize.X;
            int h = outputSize.Y;

            // Get the windings..
            var windings = new int[contourCount];
            for (int i = 0; i < shape.contours.Length; i++)
                windings[i] = shape.contours[i].Winding();

            Parallel.For(0, h, y =>
            {
                int row = shape.InverseYAxis ? h - y - 1 : y;
                for (int x = 0; x < w; ++x)
                {
                    double dummy = 0;
                    Vector2Double p = new Vector2Double(x + .5, y + .5) / scale - translate;

                    // Find the minimum absolute distance to any edge
                    double minAbsDist = double.PositiveInfinity;
                    foreach (var contour in shape.contours)
                    {
                        foreach (var edge in contour.edges)
                        {
                            SignedDistance distance = edge.GetSignedDistance(p, out dummy);
                            double absDist = Math.Abs(distance.distance);
                            if (absDist < minAbsDist)
                                minAbsDist = absDist;
                        }
                    }

                    // Compute total winding number using non-zero rule
                    int totalWinding = 0;
                    foreach (var contour in shape.contours)
                        totalWinding += ComputeWindingNumber(contour, p);

                    // Inside if winding is non-zero
                    bool inside = totalWinding != 0;
                    double sd = inside ? minAbsDist : -minAbsDist;

                    // Set the SDF value in the output image (R8 format)
                    sd /= (range * 2.0f);
                    sd = Math.Clamp(sd, -0.5, 0.5) + 0.5;

                    output.Set(
                        x + outputPosition.X,
                        row + outputPosition.Y,
                        (byte)(sd * 255.0f));
                }
            });
        }
    }
}
