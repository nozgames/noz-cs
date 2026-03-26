//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Converts SpritePath anchors directly to Clipper2 linear polygons.
//  Quadratic bezier curves are sampled inline — no MSDF intermediary.
//

using System;
using System.Numerics;
using Clipper2Lib;

namespace NoZ.Editor;

internal static class SpritePathClipper
{
    const int DefaultStepsPerCurve = 8;
    const int ClipperPrecision = 6;

    internal static PathsD SpritePathToPaths(SpritePath spritePath, int stepsPerCurve = DefaultStepsPerCurve)
    {
        if (spritePath.Anchors.Count < 3)
            return new PathsD();

        var path = AnchorsToPath(spritePath, stepsPerCurve);
        if (path.Count < 3)
            return new PathsD();

        // Ensure positive winding for NonZero fill rule.
        if (ComputeSignedArea(path) < 0)
            path.Reverse();

        // Self-union to resolve any self-intersections.
        var paths = new PathsD { path };
        var tree = new PolyTreeD();
        Clipper.BooleanOp(ClipType.Union, paths, null, tree, FillRule.NonZero, ClipperPrecision);

        var result = new PathsD();
        CollectPaths(tree, result);
        return result;
    }

    static PathD AnchorsToPath(SpritePath spritePath, int stepsPerCurve)
    {
        var path = new PathD();
        var hasTransform = spritePath.HasTransform;
        var transform = hasTransform ? spritePath.PathTransform : Matrix3x2.Identity;
        var count = spritePath.Anchors.Count;
        var segmentCount = spritePath.Open ? count - 1 : count;

        for (var a = 0; a < segmentCount; a++)
        {
            var anchor0 = spritePath.Anchors[a];
            var anchor1 = spritePath.Anchors[(a + 1) % count];

            var pos0 = hasTransform ? Vector2.Transform(anchor0.Position, transform) : anchor0.Position;
            var pos1 = hasTransform ? Vector2.Transform(anchor1.Position, transform) : anchor1.Position;

            if (Math.Abs(anchor0.Curve) < 0.0001)
            {
                path.Add(new PointD(pos0.X, pos0.Y));
            }
            else
            {
                // Compute control point in local space, then transform.
                var localMid = (anchor0.Position + anchor1.Position) * 0.5f;
                var localDir = anchor1.Position - anchor0.Position;
                var perpMag = localDir.Length();
                var localPerp = perpMag > 1e-5f
                    ? new Vector2(-localDir.Y, localDir.X) / perpMag
                    : new Vector2(0, 1);
                var localCp = localMid + localPerp * anchor0.Curve;
                var cp = hasTransform ? Vector2.Transform(localCp, transform) : localCp;

                // Sample quadratic bezier: (1-t)^2*p0 + 2(1-t)t*cp + t^2*p1
                for (int i = 0; i < stepsPerCurve; i++)
                {
                    double t = (double)i / stepsPerCurve;
                    double u = 1.0 - t;
                    double x = u * u * pos0.X + 2 * u * t * cp.X + t * t * pos1.X;
                    double y = u * u * pos0.Y + 2 * u * t * cp.Y + t * t * pos1.Y;
                    path.Add(new PointD(x, y));
                }
            }
        }

        return path;
    }

    // Shoelace formula: positive = CCW, negative = CW.
    static double ComputeSignedArea(PathD path)
    {
        double area = 0;
        for (int i = 0; i < path.Count; i++)
        {
            int j = (i + 1) % path.Count;
            area += path[i].x * path[j].y;
            area -= path[j].x * path[i].y;
        }
        return area * 0.5;
    }

    static void CollectPaths(PolyPathD node, PathsD result)
    {
        if (node.Polygon != null && node.Polygon.Count >= 3)
            result.Add(node.Polygon);

        for (int i = 0; i < node.Count; i++)
            CollectPaths(node[i], result);
    }
}
