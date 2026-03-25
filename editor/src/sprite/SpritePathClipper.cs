//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Bridge between SpritePath anchors and Clipper2 linear polygons.
//  Routes through MSDF Shape as an intermediate because MSDF handles
//  quadratic bezier curves natively for self-union, while Clipper2
//  only operates on linear segments. This preserves curve fidelity
//  during the union step before linearizing for boolean ops.
//

using System.Numerics;
using Clipper2Lib;
using NoZ.Editor.Msdf;

namespace NoZ.Editor;

internal static class SpritePathClipper
{
    const int DefaultStepsPerCurve = 8;

    internal static void AppendContour(Shape shape, SpritePath spritePath)
    {
        if (spritePath.Anchors.Count < 3) return;

        var hasTransform = spritePath.HasTransform;
        var transform = hasTransform ? spritePath.PathTransform : Matrix3x2.Identity;

        var contour = shape.AddContour();
        var count = spritePath.Anchors.Count;
        var segmentCount = spritePath.Open ? count - 1 : count;

        for (var a = 0; a < segmentCount; a++)
        {
            var anchor0 = spritePath.Anchors[a];
            var anchor1 = spritePath.Anchors[(a + 1) % count];

            var pos0 = hasTransform ? Vector2.Transform(anchor0.Position, transform) : anchor0.Position;
            var pos1 = hasTransform ? Vector2.Transform(anchor1.Position, transform) : anchor1.Position;

            var p0 = new Vector2Double(pos0.X, pos0.Y);
            var p1 = new Vector2Double(pos1.X, pos1.Y);

            if (Math.Abs(anchor0.Curve) < 0.0001)
            {
                contour.AddEdge(new LinearSegment(p0, p1));
            }
            else
            {
                // Compute control point in local space, then transform.
                // Using transformed endpoints with the untransformed curve magnitude
                // gives wrong results when the transform includes scale.
                var localMid = (anchor0.Position + anchor1.Position) * 0.5f;
                var localDir = anchor1.Position - anchor0.Position;
                var perpMag = localDir.Length();
                var localPerp = perpMag > 1e-5f
                    ? new Vector2(-localDir.Y, localDir.X) / perpMag
                    : new Vector2(0, 1);
                var localCp = localMid + localPerp * anchor0.Curve;
                var cpWorld = hasTransform ? Vector2.Transform(localCp, transform) : localCp;
                var cp = new Vector2Double(cpWorld.X, cpWorld.Y);
                contour.AddEdge(new QuadraticSegment(p0, cp, p1));
            }
        }

        if (contour.Winding() < 0)
            contour.Reverse();
    }

    internal static PathsD SpritePathToPaths(SpritePath spritePath, int stepsPerCurve = DefaultStepsPerCurve)
    {
        var msdfShape = new Shape();
        AppendContour(msdfShape, spritePath);
        msdfShape = ShapeClipper.Union(msdfShape, stepsPerCurve);
        return ShapeClipper.ShapeToPaths(msdfShape, stepsPerCurve);
    }
}
