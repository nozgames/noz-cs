//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public static class Delaunay
{
    private struct Triangle(int a, int b, int c)
    {
        public int A = a;
        public int B = b;
        public int C = c;
    }

    private struct Edge(int a, int b)
    {
        public int A = a;
        public int B = b;

        public bool Equals(Edge other) =>
            (A == other.A && B == other.B) || (A == other.B && B == other.A);
    }

    public static void Triangulate(ReadOnlySpan<Vector2> points, List<ushort> indices)
    {
        indices.Clear();

        if (points.Length < 3)
            return;

        // Find bounding box
        var min = points[0];
        var max = points[0];
        for (var i = 1; i < points.Length; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }

        var dx = max.X - min.X;
        var dy = max.Y - min.Y;
        var dmax = MathF.Max(dx, dy);
        var midX = (min.X + max.X) * 0.5f;
        var midY = (min.Y + max.Y) * 0.5f;

        // Super-triangle vertices (indices: n, n+1, n+2)
        var n = points.Length;
        var allPoints = new Vector2[n + 3];
        points.CopyTo(allPoints);
        allPoints[n] = new Vector2(midX - 20f * dmax, midY - dmax);
        allPoints[n + 1] = new Vector2(midX, midY + 20f * dmax);
        allPoints[n + 2] = new Vector2(midX + 20f * dmax, midY - dmax);

        var triangles = new List<Triangle> { new(n, n + 1, n + 2) };
        var badTriangles = new List<int>();
        var polygon = new List<Edge>();

        Log.Info($"[Delaunay] n={n} dmax={dmax} mid=({midX},{midY}) super=({allPoints[n]}, {allPoints[n+1]}, {allPoints[n+2]})");

        // Insert each point
        for (var i = 0; i < n; i++)
        {
            var p = allPoints[i];
            badTriangles.Clear();

            // Find all triangles whose circumcircle contains the point
            for (var j = 0; j < triangles.Count; j++)
            {
                if (InCircumcircle(allPoints, triangles[j], p))
                    badTriangles.Add(j);
            }

            if (i < 5 || (i == n - 1))
                Log.Info($"[Delaunay] point[{i}]=({p.X},{p.Y}) bad={badTriangles.Count} triangles={triangles.Count}");

            // Find the boundary polygon of the bad triangles
            polygon.Clear();
            for (var j = 0; j < badTriangles.Count; j++)
            {
                var t = triangles[badTriangles[j]];
                Edge[] edges = [new(t.A, t.B), new(t.B, t.C), new(t.C, t.A)];

                foreach (var edge in edges)
                {
                    var shared = false;
                    for (var k = 0; k < badTriangles.Count; k++)
                    {
                        if (k == j) continue;
                        var other = triangles[badTriangles[k]];
                        Edge[] otherEdges = [new(other.A, other.B), new(other.B, other.C), new(other.C, other.A)];
                        foreach (var oe in otherEdges)
                        {
                            if (edge.Equals(oe))
                            {
                                shared = true;
                                break;
                            }
                        }
                        if (shared) break;
                    }

                    if (!shared)
                        polygon.Add(edge);
                }
            }

            // Remove bad triangles (reverse order to preserve indices)
            badTriangles.Sort();
            for (var j = badTriangles.Count - 1; j >= 0; j--)
                triangles.RemoveAt(badTriangles[j]);

            // Re-triangulate the polygon with the new point
            foreach (var edge in polygon)
                triangles.Add(new Triangle(edge.A, edge.B, i));
        }

        // Output triangles that don't reference super-triangle vertices
        var totalTriangles = triangles.Count;
        var superRefCount = 0;
        foreach (var t in triangles)
        {
            if (t.A >= n || t.B >= n || t.C >= n)
            {
                superRefCount++;
                continue;
            }

            indices.Add((ushort)t.A);
            indices.Add((ushort)t.B);
            indices.Add((ushort)t.C);
        }
        Log.Info($"[Delaunay] points={n} totalTriangles={totalTriangles} superRef={superRefCount} output={indices.Count / 3}");
    }

    private static bool InCircumcircle(Vector2[] points, Triangle t, Vector2 p)
    {
        var a = points[t.A];
        var b = points[t.B];
        var c = points[t.C];

        double ax = a.X - p.X;
        double ay = a.Y - p.Y;
        double bx = b.X - p.X;
        double by = b.Y - p.Y;
        double cx = c.X - p.X;
        double cy = c.Y - p.Y;

        var det = ax * (by * (cx * cx + cy * cy) - cy * (bx * bx + by * by))
                - bx * (ay * (cx * cx + cy * cy) - cy * (ax * ax + ay * ay))
                + cx * (ay * (bx * bx + by * by) - by * (ax * ax + ay * ay));

        // Sign of the cross product tells us the winding order of the triangle.
        // If CW (negative cross), the determinant sign is flipped.
        var cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        return cross > 0 ? det > 0 : det < 0;
    }
}
