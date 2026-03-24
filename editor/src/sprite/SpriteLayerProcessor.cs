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

internal readonly struct LayerPathResult(PathsD Contours, Color32 Color, bool IsStroke)
{
    public readonly PathsD Contours = Contours;
    public readonly Color32 Color = Color;
    public readonly bool IsStroke = IsStroke;
}

internal static class SpriteLayerProcessor
{
    const int ClipperPrecision = 6;

    private static bool HasClipPaths(SpriteLayer layer)
    {
        for (int i = 0; i < layer.Children.Count; i++)
            if (layer.Children[i] is SpritePath { IsClip: true })
                return true;
        return false;
    }

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
                    r.Contours, above, FillRule.NonZero, precision: ClipperPrecision);
                if (trimmed.Count > 0)
                    results[i] = new LayerPathResult(trimmed, r.Color, r.IsStroke);
            }

            if (isOpaque && r.Contours.Count > 0)
            {
                above = above == null
                    ? new PathsD(r.Contours)
                    : Clipper.BooleanOp(ClipType.Union,
                        above, r.Contours, FillRule.NonZero, precision: ClipperPrecision);
            }
        }
    }

    internal static void ProcessLayer(SpriteLayer layer, List<LayerPathResult> output)
    {
        if (!layer.Visible) return;

        var needsAccumulation = HasClipPaths(layer);
        PathsD? accumulatedPaths = null;
        var results = new List<LayerPathResult>();

        // Single reverse pass: first-in-list renders on top (matches outliner order).
        // Paths and child layers are interleaved so layer ordering is preserved.
        for (int ci = layer.Children.Count - 1; ci >= 0; ci--)
        {
            var child = layer.Children[ci];

            // Child layer: capture output into results so parent subtract/clip can affect it
            if (child is SpriteLayer childLayer)
            {
                ProcessLayer(childLayer, results);
                continue;
            }

            if (child is not SpritePath path) continue;
            if (path.Anchors.Count < 3) continue;

            // Subtract: retroactively cut from all already-emitted results below
            if (path.IsSubtract)
            {
                var subContours = SpritePathClipper.SpritePathToPaths(path);
                if (subContours.Count > 0 && results.Count > 0)
                {
                    for (int i = results.Count - 1; i >= 0; i--)
                    {
                        var diff = Clipper.BooleanOp(ClipType.Difference,
                            results[i].Contours, subContours, FillRule.NonZero, precision: ClipperPrecision);
                        if (diff.Count > 0)
                            results[i] = new LayerPathResult(diff, results[i].Color, results[i].IsStroke);
                        else
                            results.RemoveAt(i);
                    }

                    if (needsAccumulation && accumulatedPaths is { Count: > 0 })
                        accumulatedPaths = Clipper.BooleanOp(ClipType.Difference,
                            accumulatedPaths, subContours, FillRule.NonZero, precision: ClipperPrecision);
                }
                continue;
            }

            var contours = SpritePathClipper.SpritePathToPaths(path);
            if (contours.Count == 0) continue;

            // Contracted paths for stroked shapes -- computed once, reused for
            // both accumulation (clip ops use fill boundary) and stroke emission.
            PathsD? contracted = null;

            // Clip: intersect with accumulated paths below (already processed due to reverse iteration)
            if (path.IsClip)
            {
                if (accumulatedPaths is not { Count: > 0 }) continue;
                contours = Clipper.BooleanOp(ClipType.Intersection,
                    contours, accumulatedPaths, FillRule.NonZero, precision: ClipperPrecision);
                if (contours.Count == 0) continue;
            }
            else
            {
                if (needsAccumulation)
                {
                    var accContours = contours;
                    if (path.StrokeColor.A > 0 && path.StrokeWidth > 0)
                    {
                        contracted = Clipper.InflatePaths(contours,
                            -(path.StrokeWidth * SpritePath.StrokeScale),
                            JoinType.Round, EndType.Polygon, precision: ClipperPrecision);
                        if (contracted.Count > 0)
                            accContours = contracted;
                    }

                    if (accumulatedPaths == null)
                        accumulatedPaths = new PathsD(accContours);
                    else
                        accumulatedPaths = Clipper.BooleanOp(ClipType.Union,
                            accumulatedPaths, accContours, FillRule.NonZero, precision: ClipperPrecision);
                }
            }

            var hasStroke = path.StrokeColor.A > 0 && path.StrokeWidth > 0;
            var hasFill = path.FillColor.A > 0;

            if (hasStroke)
            {
                contracted ??= Clipper.InflatePaths(contours,
                    -(path.StrokeWidth * SpritePath.StrokeScale),
                    JoinType.Round, EndType.Polygon, precision: ClipperPrecision);

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
                            contours, contracted, FillRule.NonZero, precision: ClipperPrecision);
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

        // Flush remaining results (paths above all child layers = top of outliner)
        TrimOverlaps(results);
        output.AddRange(results);
    }
}
