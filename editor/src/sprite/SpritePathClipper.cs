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
    const int ClipperPrecision = 6;

    internal static PathsD SpritePathToPaths(SpritePath spritePath)
    {
        var hasTransform = spritePath.HasTransform;
        var transform = hasTransform ? spritePath.PathTransform : Matrix3x2.Identity;
        var allPaths = new PathsD();

        foreach (var contour in spritePath.Contours)
        {
            if (contour.Anchors.Count < 3) continue;

            var path = ContourToPath(contour, hasTransform, transform);
            if (path.Count < 3) continue;

            // Ensure positive winding for NonZero fill rule.
            if (ComputeSignedArea(path) < 0)
                path.Reverse();

            allPaths.Add(path);
        }

        if (allPaths.Count == 0)
            return new PathsD();

        // Self-union to resolve any self-intersections.
        var tree = new PolyTreeD();
        Clipper.BooleanOp(ClipType.Union, allPaths, null, tree, FillRule.NonZero, ClipperPrecision);

        var result = new PathsD();
        CollectPaths(tree, result);
        return result;
    }

    internal static SpritePath PathsToSpritePath(PathsD paths, Color32 fillColor, Color32 strokeColor = default, byte strokeWidth = 0, SpritePathOperation operation = SpritePathOperation.Normal, SpriteStrokeJoin strokeJoin = SpriteStrokeJoin.Round)
    {
        var spritePath = new SpritePath
        {
            FillColor = fillColor,
            StrokeColor = strokeColor,
            StrokeWidth = strokeWidth,
            StrokeJoin = strokeJoin,
            Operation = operation,
        };

        for (var pi = 0; pi < paths.Count; pi++)
        {
            var clipperPath = paths[pi];
            if (clipperPath.Count < 3) continue;

            var contour = pi == 0 ? spritePath.Contours[0] : new SpriteContour();

            foreach (var pt in clipperPath)
                contour.Anchors.Add(new SpritePathAnchor { Position = new Vector2((float)pt.x, (float)pt.y) });

            if (pi > 0)
                spritePath.Contours.Add(contour);
        }

        spritePath.MarkDirty();
        return spritePath;
    }

    static PathD ContourToPath(SpriteContour contour, bool hasTransform, Matrix3x2 transform)
    {
        var path = new PathD();
        var count = contour.Anchors.Count;
        var segmentCount = contour.Open ? count - 1 : count;

        for (var a = 0; a < segmentCount; a++)
        {
            var anchor0 = contour.Anchors[a];
            var anchor1 = contour.Anchors[(a + 1) % count];

            var pos0 = hasTransform ? Vector2.Transform(anchor0.Position, transform) : anchor0.Position;
            var pos1 = hasTransform ? Vector2.Transform(anchor1.Position, transform) : anchor1.Position;

            var localDir = anchor1.Position - anchor0.Position;
            var segLen = localDir.Length();
            var steps = SpritePath.ComputeSegmentSamples(anchor0.Curve, segLen);

            if (steps == 0)
            {
                path.Add(new PointD(pos0.X, pos0.Y));
            }
            else
            {
                var localMid = (anchor0.Position + anchor1.Position) * 0.5f;
                var localPerp = segLen > 1e-5f
                    ? new Vector2(-localDir.Y, localDir.X) / segLen
                    : new Vector2(0, 1);
                var localCp = localMid + localPerp * anchor0.Curve;
                var cp = hasTransform ? Vector2.Transform(localCp, transform) : localCp;

                // Anchor point + interior samples matching SpriteContour spacing
                path.Add(new PointD(pos0.X, pos0.Y));
                for (int i = 0; i < steps; i++)
                {
                    double t = (double)(i + 1) / (steps + 1);
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
