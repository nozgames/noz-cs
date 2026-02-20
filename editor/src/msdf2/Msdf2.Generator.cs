//
//  Faithful port of msdfgen by Viktor Chlumsky
//  https://github.com/Chlumsky/msdfgen
//

using System;
using System.Threading.Tasks;
using static NoZ.Editor.Msdf2.MsdfMath;

namespace NoZ.Editor.Msdf2;

/// <summary>
/// MSDF bitmap: width * height * 3 floats (RGB channels).
/// </summary>
internal class MsdfBitmap
{
    public readonly int width;
    public readonly int height;
    public readonly float[] pixels; // row-major, 3 floats per pixel (R, G, B)

    public MsdfBitmap(int width, int height)
    {
        this.width = width;
        this.height = height;
        pixels = new float[width * height * 3];
    }

    public Span<float> this[int x, int y] => pixels.AsSpan((y * width + x) * 3, 3);
}

internal static class MsdfGenerator
{
    /// <summary>
    /// Generate a multi-channel signed distance field using the legacy algorithm.
    /// This is a direct, faithful port of msdfgen's generateMSDF_legacy.
    ///
    /// scale: pixels per shape unit
    /// translate: shape-space offset to apply before scaling
    /// range: the distance range in shape units (e.g. Range(-4, 4) means 8 units total)
    /// </summary>
    public static void GenerateMSDF(
        MsdfBitmap output,
        Shape shape,
        double rangeValue,
        Vector2Double scale,
        Vector2Double translate)
    {
        double rangeLower = -0.5 * rangeValue;
        double rangeUpper = 0.5 * rangeValue;
        double rangeWidth = rangeUpper - rangeLower;
        double distScale = 1.0 / rangeWidth;
        double distTranslate = -rangeLower;

        int w = output.width;
        int h = output.height;
        bool flipY = shape.inverseYAxis;

        Parallel.For(0, h, y =>
        {
            int row = flipY ? h - 1 - y : y;

            for (int x = 0; x < w; ++x)
            {
                var p = new Vector2Double(x + 0.5, y + 0.5) / scale - translate;

                SignedDistance rMinDist = new(), gMinDist = new(), bMinDist = new();
                EdgeSegment? rNearEdge = null, gNearEdge = null, bNearEdge = null;
                double rNearParam = 0, gNearParam = 0, bNearParam = 0;

                foreach (var contour in shape.contours)
                {
                    foreach (var edge in contour.edges)
                    {
                        var distance = edge.GetSignedDistance(p, out double param);

                        if (((int)edge.color & (int)EdgeColor.RED) != 0 && distance < rMinDist)
                        {
                            rMinDist = distance;
                            rNearEdge = edge;
                            rNearParam = param;
                        }
                        if (((int)edge.color & (int)EdgeColor.GREEN) != 0 && distance < gMinDist)
                        {
                            gMinDist = distance;
                            gNearEdge = edge;
                            gNearParam = param;
                        }
                        if (((int)edge.color & (int)EdgeColor.BLUE) != 0 && distance < bMinDist)
                        {
                            bMinDist = distance;
                            bNearEdge = edge;
                            bNearParam = param;
                        }
                    }
                }

                rNearEdge?.DistanceToPerpendicularDistance(ref rMinDist, p, rNearParam);
                gNearEdge?.DistanceToPerpendicularDistance(ref gMinDist, p, gNearParam);
                bNearEdge?.DistanceToPerpendicularDistance(ref bMinDist, p, bNearParam);

                var pixel = output[x, row];
                pixel[0] = (float)(distScale * (rMinDist.distance + distTranslate));
                pixel[1] = (float)(distScale * (gMinDist.distance + distTranslate));
                pixel[2] = (float)(distScale * (bMinDist.distance + distTranslate));
            }
        });
    }

    /// <summary>
    /// Apply the legacy error correction to the MSDF bitmap.
    /// Detects clashing texels (where interpolation between adjacent pixels would produce
    /// incorrect results) and replaces them with the median of their channels.
    /// </summary>
    public static void ErrorCorrection(MsdfBitmap sdf, Vector2Double threshold)
    {
        int w = sdf.width, h = sdf.height;
        var clashes = new System.Collections.Concurrent.ConcurrentBag<(int x, int y)>();

        // Detect cardinal clashes
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; ++x)
            {
                var px = sdf[x, y];
                if ((x > 0 && DetectClash(px, sdf[x - 1, y], threshold.x)) ||
                    (x < w - 1 && DetectClash(px, sdf[x + 1, y], threshold.x)) ||
                    (y > 0 && DetectClash(px, sdf[x, y - 1], threshold.y)) ||
                    (y < h - 1 && DetectClash(px, sdf[x, y + 1], threshold.y)))
                {
                    clashes.Add((x, y));
                }
            }
        });

        foreach (var (cx, cy) in clashes)
        {
            var pixel = sdf[cx, cy];
            float med = MathF.Max(MathF.Min(pixel[0], pixel[1]), MathF.Min(MathF.Max(pixel[0], pixel[1]), pixel[2]));
            pixel[0] = med;
            pixel[1] = med;
            pixel[2] = med;
        }

        // Detect diagonal clashes
        clashes.Clear();
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; ++x)
            {
                var px = sdf[x, y];
                double diagThreshold = threshold.x + threshold.y;
                if ((x > 0 && y > 0 && DetectClash(px, sdf[x - 1, y - 1], diagThreshold)) ||
                    (x < w - 1 && y > 0 && DetectClash(px, sdf[x + 1, y - 1], diagThreshold)) ||
                    (x > 0 && y < h - 1 && DetectClash(px, sdf[x - 1, y + 1], diagThreshold)) ||
                    (x < w - 1 && y < h - 1 && DetectClash(px, sdf[x + 1, y + 1], diagThreshold)))
                {
                    clashes.Add((x, y));
                }
            }
        });

        foreach (var (cx, cy) in clashes)
        {
            var pixel = sdf[cx, cy];
            float med = MathF.Max(MathF.Min(pixel[0], pixel[1]), MathF.Min(MathF.Max(pixel[0], pixel[1]), pixel[2]));
            pixel[0] = med;
            pixel[1] = med;
            pixel[2] = med;
        }
    }

    private static bool DetectClash(Span<float> a, Span<float> b, double threshold)
    {
        float a0 = a[0], a1 = a[1], a2 = a[2];
        float b0 = b[0], b1 = b[1], b2 = b[2];

        // Sort channels so that pairs go from biggest to smallest absolute difference
        if (MathF.Abs(b0 - a0) < MathF.Abs(b1 - a1))
        {
            (a0, a1) = (a1, a0);
            (b0, b1) = (b1, b0);
        }
        if (MathF.Abs(b1 - a1) < MathF.Abs(b2 - a2))
        {
            (a1, a2) = (a2, a1);
            (b1, b2) = (b2, b1);
            if (MathF.Abs(b0 - a0) < MathF.Abs(b1 - a1))
            {
                (a0, a1) = (a1, a0);
                (b0, b1) = (b1, b0);
            }
        }

        return (MathF.Abs(b1 - a1) >= threshold) &&
            !(b0 == b1 && b0 == b2) && // Ignore if other pixel has been equalized
            MathF.Abs(a2 - 0.5f) >= MathF.Abs(b2 - 0.5f); // Only flag pixel farther from edge
    }
}
