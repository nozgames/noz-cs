//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Clipper2Lib;
using NoZ.Editor.Msdf;

namespace NoZ.Editor;

internal static class SpritePathClipper
{
    const int DefaultStepsPerCurve = 8;

    internal static void AppendContour(Shape shape, SpritePath spritePath)
    {
        if (spritePath.Anchors.Count < 3) return;

        var contour = shape.AddContour();
        var count = spritePath.Anchors.Count;
        var segmentCount = spritePath.Open ? count - 1 : count;

        for (var a = 0; a < segmentCount; a++)
        {
            var anchor0 = spritePath.Anchors[a];
            var anchor1 = spritePath.Anchors[(a + 1) % count];

            var p0 = new Vector2Double(anchor0.Position.X, anchor0.Position.Y);
            var p1 = new Vector2Double(anchor1.Position.X, anchor1.Position.Y);

            if (Math.Abs(anchor0.Curve) < 0.0001)
            {
                contour.AddEdge(new LinearSegment(p0, p1));
            }
            else
            {
                var mid = new Vector2Double(
                    0.5 * (p0.x + p1.x),
                    0.5 * (p0.y + p1.y));
                var dir = p1 - p0;
                double perpMag = MsdfMath.Length(dir);
                Vector2Double perp;
                if (perpMag > 1e-10)
                    perp = new Vector2Double(-dir.y / perpMag, dir.x / perpMag);
                else
                    perp = new Vector2Double(0, 1);
                var cp = mid + perp * anchor0.Curve;
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
