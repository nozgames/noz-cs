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
    internal static void ProcessLayer(
        SpriteLayer layer,
        Action<LayerPathResult> emit)
    {
        if (!layer.Visible) return;

        // Collect and pre-merge subtract contours for this layer
        PathsD? mergedSubtracts = null;
        foreach (var child in layer.Children)
        {
            if (child is not SpritePath path) continue;
            if (!path.IsSubtract || path.Anchors.Count < 3) continue;
            var contours = SpritePathClipper.SpritePathToPaths(path);
            if (contours.Count > 0)
            {
                mergedSubtracts ??= new PathsD();
                mergedSubtracts.AddRange(contours);
            }
        }

        PathsD? accumulatedPaths = null;

        foreach (var child in layer.Children)
        {
            if (child is not SpritePath path) continue;
            if (path.IsSubtract || path.Anchors.Count < 3) continue;

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

            // Apply pre-merged subtract paths
            if (mergedSubtracts is { Count: > 0 })
            {
                contours = Clipper.BooleanOp(ClipType.Difference,
                    contours, mergedSubtracts, FillRule.NonZero, precision: 6);
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
                    emit(new LayerPathResult(contours, path.StrokeColor, true));
                    if (contracted.Count > 0)
                        emit(new LayerPathResult(contracted, path.FillColor, false));
                }
                else
                {
                    if (contracted.Count > 0)
                    {
                        var ring = Clipper.BooleanOp(ClipType.Difference,
                            contours, contracted, FillRule.NonZero, precision: 6);
                        if (ring.Count > 0)
                            emit(new LayerPathResult(ring, path.StrokeColor, true));
                    }
                    else
                    {
                        emit(new LayerPathResult(contours, path.StrokeColor, true));
                    }
                }
            }
            else if (hasFill)
            {
                emit(new LayerPathResult(contours, path.FillColor, false));
            }
        }

        // Recurse into child layers
        foreach (var child in layer.Children)
        {
            if (child is SpriteLayer childLayer)
                ProcessLayer(childLayer, emit);
        }
    }
}
