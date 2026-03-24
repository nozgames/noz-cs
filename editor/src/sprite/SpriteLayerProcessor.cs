//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Shared layer-scoped boolean operation logic used by both
//  rasterization (SpriteDocument) and tessellation (SpriteEditor.Mesh).
//
//  The MSDF bridge in SpritePathClipper exists because Clipper2 only
//  operates on linear segments, while MSDF's Shape handles quadratic
//  beziers natively — preserving curve fidelity during self-union.
//

using Clipper2Lib;

namespace NoZ.Editor;

internal readonly struct LayerPathResult
{
    public readonly PathsD Contours;
    public readonly Color32 Color;
    public readonly bool IsStroke;

    public LayerPathResult(PathsD contours, Color32 color, bool isStroke)
    {
        Contours = contours;
        Color = color;
        IsStroke = isStroke;
    }
}

internal static class SpriteLayerProcessor
{
    private static void TrimOverlaps(List<LayerPathResult> results)
    {
        if (results.Count < 2) return;

        // Process from top (last) to bottom (first), building cumulative "above" contours.
        // Each lower opaque path gets its overlapping region with higher paths subtracted,
        // creating clean geometric boundaries for the rasterizer's AA.
        PathsD? above = null;
        for (int i = results.Count - 1; i >= 0; i--)
        {
            var r = results[i];
            bool isOpaque = r.Color.A == 255;

            if (above != null && isOpaque && r.Contours.Count > 0)
            {
                var trimmed = Clipper.BooleanOp(ClipType.Difference,
                    r.Contours, above, FillRule.NonZero, precision: 6);
                if (trimmed.Count > 0)
                    results[i] = new LayerPathResult(trimmed, r.Color, r.IsStroke);
            }

            if (isOpaque && r.Contours.Count > 0)
            {
                above = above == null
                    ? new PathsD(r.Contours)
                    : Clipper.BooleanOp(ClipType.Union,
                        above, r.Contours, FillRule.NonZero, precision: 6);
            }
        }
    }

    internal static void ProcessLayer(
        SpriteLayer layer,
        Action<LayerPathResult> emit)
    {
        if (!layer.Visible) return;

        PathsD? accumulatedPaths = null;
        PathsD? accumulatedSubtracts = null;
        var results = new List<LayerPathResult>();

        foreach (var child in layer.Children)
        {
            if (child is not SpritePath path) continue;
            if (path.Anchors.Count < 3) continue;

            if (path.IsSubtract)
            {
                var subContours = SpritePathClipper.SpritePathToPaths(path);
                if (subContours.Count > 0)
                {
                    accumulatedSubtracts ??= new PathsD();
                    accumulatedSubtracts.AddRange(subContours);
                }
                continue;
            }

            var contours = SpritePathClipper.SpritePathToPaths(path);
            if (contours.Count == 0) continue;

            if (path.IsClip)
            {
                if (accumulatedPaths is not { Count: > 0 }) continue;
                contours = Clipper.BooleanOp(ClipType.Intersection,
                    contours, accumulatedPaths, FillRule.NonZero, precision: 6);
                if (contours.Count == 0) continue;
            }
            else
            {
                var accContours = contours;
                if (path.StrokeColor.A > 0 && path.StrokeWidth > 0)
                {
                    var halfStroke = path.StrokeWidth * SpritePath.StrokeScale;
                    var contracted = Clipper.InflatePaths(contours, -halfStroke,
                        JoinType.Round, EndType.Polygon, precision: 6);
                    if (contracted.Count > 0)
                        accContours = contracted;
                }

                if (accumulatedPaths == null)
                    accumulatedPaths = new PathsD(accContours);
                else
                    accumulatedPaths = Clipper.BooleanOp(ClipType.Union,
                        accumulatedPaths, accContours, FillRule.NonZero, precision: 6);
            }

            // Apply subtract paths that appeared before this path
            if (accumulatedSubtracts is { Count: > 0 })
            {
                contours = Clipper.BooleanOp(ClipType.Difference,
                    contours, accumulatedSubtracts, FillRule.NonZero, precision: 6);
                if (contours.Count == 0) continue;
            }

            var hasStroke = path.StrokeColor.A > 0 && path.StrokeWidth > 0;
            var hasFill = path.FillColor.A > 0;

            if (hasStroke)
            {
                var halfStroke = path.StrokeWidth * SpritePath.StrokeScale;
                var contracted = Clipper.InflatePaths(contours, -halfStroke,
                    JoinType.Round, EndType.Polygon, precision: 6);

                if (hasFill)
                {
                    results.Add(new LayerPathResult(contours, path.StrokeColor, true));
                    if (contracted.Count > 0)
                        results.Add(new LayerPathResult(contracted, path.FillColor, false));
                }
                else
                {
                    if (contracted.Count > 0)
                    {
                        var ring = Clipper.BooleanOp(ClipType.Difference,
                            contours, contracted, FillRule.NonZero, precision: 6);
                        if (ring.Count > 0)
                            results.Add(new LayerPathResult(ring, path.StrokeColor, true));
                    }
                    else
                    {
                        results.Add(new LayerPathResult(contours, path.StrokeColor, true));
                    }
                }
            }
            else if (hasFill)
            {
                results.Add(new LayerPathResult(contours, path.FillColor, false));
            }
        }

        // Remove geometric overlaps between opaque paths to prevent AA color bleed.
        // Lower paths get trimmed where higher paths cover them, so the rasterizer
        // never alpha-blends overlapping opaque regions.
        TrimOverlaps(results);

        foreach (var result in results)
            emit(result);

        // Recurse into child layers
        foreach (var child in layer.Children)
        {
            if (child is SpriteLayer childLayer)
                ProcessLayer(childLayer, emit);
        }
    }
}
