//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using Clipper2Lib;

namespace NoZ.Editor;

public struct NodeHitResult
{
    public SpritePath Path;
    public SpritePath.HitResult Hit;
}

public struct AnchorHitResult
{
    public SpritePath Path;
    public int ContourIndex;
    public int AnchorIndex;
    public float DistSqr;
    public Vector2 Position;
}

public struct SegmentHitResult
{
    public SpritePath Path;
    public int ContourIndex;
    public int SegmentIndex;
    public Vector2 Position;
}

public static class SpriteNodeVectorExtensions
{
    #region Path Operations

    public static void CollectVisiblePaths(this SpriteNode node, List<SpritePath> result)
    {
        if (!node.Visible) return;

        if (node is SpritePath path)
        {
            result.Add(path);
            return;
        }

        foreach (var child in node.Children)
            child.CollectVisiblePaths(result);
    }

    public static SpritePath? GetPathWithSelection(this SpriteNode node)
    {
        if (node is SpritePath path && path.HasSelection())
            return path;

        foreach (var child in node.Children)
        {
            var found = child.GetPathWithSelection();
            if (found != null)
                return found;
        }

        return null;
    }

    public static void ForEachEditablePath(this SpriteNode node, Action<SpritePath> action)
    {
        if (!node.Visible || node.Locked) return;

        if (node is SpritePath path)
        {
            action(path);
            return;
        }

        foreach (var child in node.Children)
            child.ForEachEditablePath(action);
    }

    public static bool IsEditable(this SpriteNode node)
    {
        for (var n = (SpriteNode?)node; n != null; n = n.Parent)
            if (!n.Visible || n.Locked) return false;
        return true;
    }

    public static void CollectPathsWithSelection(this SpriteNode node, List<SpritePath> result)
    {
        if (node is SpritePath path && path.HasSelection())
        {
            result.Add(path);
            return;
        }

        foreach (var child in node.Children)
            child.CollectPathsWithSelection(result);
    }

    public static void CollectSelectedGroups(this SpriteNode node, List<SpriteGroup> result)
    {
        if (node is SpriteGroup group && group.IsSelected)
        {
            result.Add(group);
            return;
        }

        foreach (var child in node.Children)
            child.CollectSelectedGroups(result);
    }

    public static void CollectSelectedPaths(this SpriteNode node, List<SpritePath> result)
    {
        if (node is SpritePath path && path.IsSelected)
        {
            result.Add(path);
            return;
        }

        foreach (var child in node.Children)
            child.CollectSelectedPaths(result);
    }

    public static void SelectPathsInRect(this SpriteNode node, Rect rect)
    {
        if (!node.Visible || node.Locked) return;

        if (node is SpritePath path)
        {
            if (path.Bounds.Intersects(rect))
                path.SelectPath();
            return;
        }

        foreach (var child in node.Children)
            child.SelectPathsInRect(rect);
    }

    public static void SelectAnchorsInRect(this SpriteNode node, Rect rect)
    {
        if (!node.Visible || node.Locked) return;

        if (node is SpritePath path)
        {
            path.SelectAnchorsInRect(rect);
            return;
        }

        foreach (var child in node.Children)
            child.SelectAnchorsInRect(rect);
    }

    #endregion

    #region Hit Testing

    public static AnchorHitResult? HitTestAnchor(this SpriteNode node, Vector2 point, bool onlySelected = false, HashSet<SpritePath>? exclude = null)
    {
        static void Recursive(SpriteNode node, Vector2 point, bool onlySelected, HashSet<SpritePath>? exclude, ref AnchorHitResult? best)
        {
            if (!node.Visible || node.Locked) return;
            if (node is SpritePath path)
            {
                if (onlySelected && !path.IsSelected) return;
                if (exclude != null && exclude.Contains(path)) return;
                var (contourIndex, anchorIndex, distSqr, pos) = path.HitTestAnchor(point);
                if (anchorIndex >= 0 && (!best.HasValue || distSqr < best.Value.DistSqr))
                    best = new AnchorHitResult { Path = path, ContourIndex = contourIndex, AnchorIndex = anchorIndex, DistSqr = distSqr, Position = pos };
                return;
            }

            for (var i = 0; i < node.Children.Count; i++)
                Recursive(node.Children[i], point, onlySelected, exclude, ref best);
        }

        AnchorHitResult? best = null;
        Recursive(node, point, onlySelected, exclude, ref best);
        return best;
    }

    public static int HitTestAnchor(this SpriteNode node, Vector2 point, List<AnchorHitResult> results, bool onlySelected = false)
    {
        static int Recursive(SpriteNode node, Vector2 point, List<AnchorHitResult> results, bool onlySelected)
        {
            if (!node.Visible || node.Locked) return 0;
            var count = 0;

            if (node is SpritePath path)
            {
                if (onlySelected && !path.IsSelected) return 0;
                var (contourIndex, anchorIndex, distSqr, pos) = path.HitTestAnchor(point);
                if (anchorIndex >= 0)
                {
                    results.Add(new AnchorHitResult { Path = path, ContourIndex = contourIndex, AnchorIndex = anchorIndex, DistSqr = distSqr, Position = pos });
                    count++;
                }
                return count;
            }

            for (var i = 0; i < node.Children.Count; i++)
                count += Recursive(node.Children[i], point, results, onlySelected);
            return count;
        }

        return Recursive(node, point, results, onlySelected);
    }

    public static SegmentHitResult? HitTestSegment(this SpriteNode node, Vector2 point, bool onlySelected = false)
    {
        static void Recursive(SpriteNode node, Vector2 point, bool onlySelected, ref SegmentHitResult? best, ref float bestDistSqr)
        {
            if (!node.Visible || node.Locked) return;

            if (node is SpritePath path)
            {
                if (onlySelected && !path.IsSelected) return;
                var (contourIndex, segmentIndex, distSqr, pos) = path.HitTestSegment(point);
                if (segmentIndex >= 0 && distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    best = new SegmentHitResult { Path = path, ContourIndex = contourIndex, SegmentIndex = segmentIndex, Position = pos };
                }
                return;
            }

            for (var i = 0; i < node.Children.Count; i++)
                Recursive(node.Children[i], point, onlySelected, ref best, ref bestDistSqr);
        }

        SegmentHitResult? best = null;
        var bestDistSqr = float.MaxValue;
        Recursive(node, point, onlySelected, ref best, ref bestDistSqr);
        return best;
    }

    public static SpritePath? HitTestPath(this SpriteNode node, Vector2 point)
    {
        static SpritePath? Recursive(SpriteNode node, Vector2 point)
        {
            if (!node.Visible || node.Locked) return null;

            if (node is SpritePath path)
                return HitTestPathWithStroke(path, point) ? path : null;

            for (var i = 0; i < node.Children.Count; i++)
            {
                var hit = Recursive(node.Children[i], point);
                if (hit != null)
                    return hit;
            }

            return null;
        }

        return Recursive(node, point);
    }

    public static int HitTestPath(this SpriteNode node, Vector2 point, List<SpritePath> results)
    {
        static int Recursive(SpriteNode node, Vector2 point, List<SpritePath> results)
        {
            if (!node.Visible || node.Locked) return 0;
            var count = 0;

            if (node is SpritePath path)
            {
                if (HitTestPathWithStroke(path, point))
                {
                    results.Add(path);
                    count++;
                }
                return count;
            }

            for (var i = 0; i < node.Children.Count; i++)
                count += Recursive(node.Children[i], point, results);
            return count;
        }

        return Recursive(node, point, results);
    }

    private static bool HitTestPathWithStroke(SpritePath path, Vector2 point)
    {
        if (!path.HitTestPath(point))
            return false;

        // Subtract paths have no visible fill or stroke but must still be selectable
        if (path.IsSubtract)
            return true;

        // Filled paths: any point inside the contour is a hit
        if (path.FillColor.A > 0)
            return true;

        // Stroke-only paths: exclude the empty interior (inside contracted boundary)
        if (path.StrokeColor.A > 0 && path.StrokeWidth > 0)
        {
            var contracted = path.GetContractedPaths();
            if (contracted is { Count: > 0 })
            {
                var p = new PointD(point.X, point.Y);
                foreach (var cp in contracted)
                {
                    if (Clipper.PointInPolygon(p, cp) != PointInPolygonResult.IsOutside)
                        return false;
                }
            }
            return true;
        }

        // No fill, no stroke — nothing visible to hit
        return false;
    }

    #endregion
}
