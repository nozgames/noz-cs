//
//  Faithful port of msdfgen by Viktor Chlumsky
//  https://github.com/Chlumsky/msdfgen
//

using System;
using System.Collections.Generic;
using static NoZ.Editor.Msdf2.MsdfMath;

namespace NoZ.Editor.Msdf2;

internal static class EdgeColoring
{
    private const int EDGE_LENGTH_PRECISION = 4;

    private static bool IsCorner(Vector2Double aDir, Vector2Double bDir, double crossThreshold)
    {
        return Dot(aDir, bDir) <= 0 || Math.Abs(Cross(aDir, bDir)) > crossThreshold;
    }

    private static int SeedExtract2(ref ulong seed)
    {
        int v = (int)(seed) & 1;
        seed >>= 1;
        return v;
    }

    private static int SeedExtract3(ref ulong seed)
    {
        int v = (int)(seed % 3);
        seed /= 3;
        return v;
    }

    private static EdgeColor InitColor(ref ulong seed)
    {
        EdgeColor[] colors = [EdgeColor.CYAN, EdgeColor.MAGENTA, EdgeColor.YELLOW];
        return colors[SeedExtract3(ref seed)];
    }

    private static void SwitchColor(ref EdgeColor color, ref ulong seed)
    {
        int shifted = (int)color << (1 + SeedExtract2(ref seed));
        color = (EdgeColor)((shifted | shifted >> 3) & (int)EdgeColor.WHITE);
    }

    private static void SwitchColor(ref EdgeColor color, ref ulong seed, EdgeColor banned)
    {
        var combined = (EdgeColor)((int)color & (int)banned);
        if (combined == EdgeColor.RED || combined == EdgeColor.GREEN || combined == EdgeColor.BLUE)
            color = (EdgeColor)((int)combined ^ (int)EdgeColor.WHITE);
        else
            SwitchColor(ref color, ref seed);
    }

    private static int SymmetricalTrichotomy(int position, int n)
    {
        return (int)(3 + 2.875 * position / (n - 1) - 1.4375 + 0.5) - 3;
    }

    /// <summary>
    /// Assigns colors to edges of the shape in accordance to the multi-channel distance field technique.
    /// May split some edges if necessary.
    /// angleThreshold specifies the maximum angle (in radians) to be considered a corner, e.g. 3 (~172 degrees).
    /// </summary>
    public static void ColorSimple(Shape shape, double angleThreshold, ulong seed = 0)
    {
        double crossThreshold = Math.Sin(angleThreshold);
        var color = InitColor(ref seed);
        var corners = new List<int>();

        foreach (var contour in shape.contours)
        {
            if (contour.edges.Count == 0)
                continue;

            // Identify corners
            corners.Clear();
            var prevDirection = contour.edges[^1].Direction(1);
            for (int index = 0; index < contour.edges.Count; ++index)
            {
                if (IsCorner(Normalize(prevDirection), Normalize(contour.edges[index].Direction(0)), crossThreshold))
                    corners.Add(index);
                prevDirection = contour.edges[index].Direction(1);
            }

            // Smooth contour
            if (corners.Count == 0)
            {
                SwitchColor(ref color, ref seed);
                foreach (var edge in contour.edges)
                    edge.color = color;
            }
            // "Teardrop" case
            else if (corners.Count == 1)
            {
                EdgeColor[] colors = new EdgeColor[3];
                SwitchColor(ref color, ref seed);
                colors[0] = color;
                colors[1] = EdgeColor.WHITE;
                SwitchColor(ref color, ref seed);
                colors[2] = color;
                int corner = corners[0];
                if (contour.edges.Count >= 3)
                {
                    int m = contour.edges.Count;
                    for (int i = 0; i < m; ++i)
                        contour.edges[(corner + i) % m].color = colors[1 + SymmetricalTrichotomy(i, m)];
                }
                else if (contour.edges.Count >= 1)
                {
                    // Less than three edge segments for three colors => edges must be split
                    var parts = new EdgeSegment[7];
                    contour.edges[0].SplitInThirds(out parts[0 + 3 * corner], out parts[1 + 3 * corner], out parts[2 + 3 * corner]);
                    if (contour.edges.Count >= 2)
                    {
                        contour.edges[1].SplitInThirds(out parts[3 - 3 * corner], out parts[4 - 3 * corner], out parts[5 - 3 * corner]);
                        parts[0].color = parts[1].color = colors[0];
                        parts[2].color = parts[3].color = colors[1];
                        parts[4].color = parts[5].color = colors[2];
                    }
                    else
                    {
                        parts[0].color = colors[0];
                        parts[1].color = colors[1];
                        parts[2].color = colors[2];
                    }
                    contour.edges.Clear();
                    for (int i = 0; i < parts.Length && parts[i] != null; ++i)
                        contour.edges.Add(parts[i]);
                }
            }
            // Multiple corners
            else
            {
                int cornerCount = corners.Count;
                int spline = 0;
                int start = corners[0];
                int m = contour.edges.Count;
                SwitchColor(ref color, ref seed);
                var initialColor = color;
                for (int i = 0; i < m; ++i)
                {
                    int index = (start + i) % m;
                    if (spline + 1 < cornerCount && corners[spline + 1] == index)
                    {
                        ++spline;
                        SwitchColor(ref color, ref seed, (EdgeColor)((spline == cornerCount - 1 ? 1 : 0) * (int)initialColor));
                    }
                    contour.edges[index].color = color;
                }
            }
        }
    }
}
