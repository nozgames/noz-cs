//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System;

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

        // Compute winding number contribution from a contour using ray casting
        // Returns +1 for each upward crossing, -1 for each downward crossing
        private static int ComputeWindingNumber(Contour contour, Vector2Double p)
        {
            int winding = 0;
            const int samples = 32; // samples per edge for curves

            foreach (var edge in contour.edges)
            {
                Vector2Double prev = edge.GetPoint(0);
                for (int s = 1; s <= samples; s++)
                {
                    Vector2Double curr = edge.GetPoint(s / (double)samples);

                    // Check if this segment crosses the horizontal ray from p going right
                    if (prev.y <= p.y)
                    {
                        if (curr.y > p.y)
                        {
                            // Upward crossing
                            double t = (p.y - prev.y) / (curr.y - prev.y);
                            double xIntersect = prev.x + t * (curr.x - prev.x);
                            if (xIntersect > p.x)
                                winding++;
                        }
                    }
                    else
                    {
                        if (curr.y <= p.y)
                        {
                            // Downward crossing
                            double t = (p.y - prev.y) / (curr.y - prev.y);
                            double xIntersect = prev.x + t * (curr.x - prev.x);
                            if (xIntersect > p.x)
                                winding--;
                        }
                    }

                    prev = curr;
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

            for (int y = 0; y < h; ++y)
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
                    // This correctly handles overlapping same-winding contours
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
            }
        }
    }
}
