//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using Clipper2Lib;

namespace NoZ.Editor;

public abstract class SpriteNode
{
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

    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public List<SpriteNode> Children { get; } = [];
    public SpriteNode? Parent { get; private set; }
    public bool Expanded { get; set; } = true;
    public bool IsSelected { get; set; }
    public virtual bool IsExpandable => false;

    public abstract SpriteNode Clone();

    protected void ClonePropertiesTo(SpriteNode target)
    {
        target.Name = Name;
        target.Visible = Visible;
        target.Locked = Locked;
        target.Expanded = Expanded;
        target.IsSelected = IsSelected;
    }

    #region Hierarchy

    public void Add(SpriteNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public void Insert(int index, SpriteNode child)
    {
        child.Parent = this;
        Children.Insert(index, child);
    }

    public void Remove(SpriteNode child)
    {
        child.Parent = null;
        Children.Remove(child);
    }

    public void RemoveFromParent() => Parent?.Remove(this);

    public void RemoveAt(int index)
    {
        Children[index].Parent = null;
        Children.RemoveAt(index);
    }

    public void Clear()
    {
        foreach (var child in Children)
            child.Parent = null;
        Children.Clear();
    }

    #endregion

    #region Tree Traversal

    public void ForEach(Action<SpriteNode> action)
    {
        action(this);
        foreach (var child in Children)
            child.ForEach(action);
    }

    public void ForEach(Action<SpriteGroup> action)
    {
        if (this is SpriteGroup group)
            action(group);
        foreach (var child in Children)
            child.ForEach(action);
    }

    public void ForEach(Action<PixelLayer> action)
    {
        if (this is PixelLayer pixel)
            action(pixel);
        foreach (var child in Children)
            child.ForEach(action);
    }

    public SpriteNode? FindNode(string name)
    {
        if (Name == name)
            return this;

        foreach (var child in Children)
        {
            var found = child.FindNode(name);
            if (found != null)
                return found;
        }

        return null;
    }

    public SpriteGroup? FindGroup(string name)
    {
        if (this is SpriteGroup group && group.Name == name)
            return group;

        foreach (var child in Children)
        {
            var found = child.FindGroup(name);
            if (found != null)
                return found;
        }

        return null;
    }

    #endregion

    #region Path Operations

    public void CollectVisiblePaths(List<SpritePath> result)
    {
        if (!Visible) return;

        if (this is SpritePath path)
        {
            result.Add(path);
            return;
        }

        foreach (var child in Children)
            child.CollectVisiblePaths(result);
    }

    public SpritePath? GetPathWithSelection()
    {
        if (this is SpritePath path && path.HasSelection())
            return path;

        foreach (var child in Children)
        {
            var found = child.GetPathWithSelection();
            if (found != null)
                return found;
        }

        return null;
    }

    public void ForEachEditablePath(Action<SpritePath> action)
    {
        if (!Visible || Locked) return;

        if (this is SpritePath path)
        {
            action(path);
            return;
        }

        foreach (var child in Children)
            child.ForEachEditablePath(action);
    }

    public void CollectPathsWithSelection(List<SpritePath> result)
    {
        if (this is SpritePath path && path.HasSelection())
        {
            result.Add(path);
            return;
        }

        foreach (var child in Children)
            child.CollectPathsWithSelection(result);
    }

    public virtual void ClearSelection()
    {
        IsSelected = false;
        foreach (var child in Children)
            child.ClearSelection();
    }

    public void CollectSelectedGroups(List<SpriteGroup> result)
    {
        if (this is SpriteGroup group && group.IsSelected)
        {
            result.Add(group);
            return;
        }

        foreach (var child in Children)
            child.CollectSelectedGroups(result);
    }

    public void CollectSelectedPaths(List<SpritePath> result)
    {
        if (this is SpritePath path && path.IsSelected)
        {
            result.Add(path);
            return;
        }

        foreach (var child in Children)
            child.CollectSelectedPaths(result);
    }

    public void SelectPathsInRect(Rect rect)
    {
        if (!Visible || Locked) return;

        if (this is SpritePath path)
        {
            if (path.Bounds.Intersects(rect))
                path.SelectPath();
            return;
        }

        foreach (var child in Children)
            child.SelectPathsInRect(rect);
    }

    public void SelectAnchorsInRect(Rect rect)
    {
        if (!Visible || Locked) return;

        if (this is SpritePath path)
        {
            path.SelectAnchorsInRect(rect);
            return;
        }

        foreach (var child in Children)
            child.SelectAnchorsInRect(rect);
    }

    #endregion

    #region Hit Testing

    public AnchorHitResult? HitTestAnchor(Vector2 point, bool onlySelected = false, HashSet<SpritePath>? exclude = null)
    {
        static void Recursive(SpriteNode node, Vector2 point, bool onlySelected, HashSet<SpritePath>? exclude, ref AnchorHitResult? best)
        {
            if (!node.Visible) return;
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
        Recursive(this, point, onlySelected, exclude, ref best);
        return best;
    }

    public int HitTestAnchor(Vector2 point, List<AnchorHitResult> results, bool onlySelected = false)
    {
        static int Recursive(SpriteNode node, Vector2 point, List<AnchorHitResult> results, bool onlySelected)
        {
            if (!node.Visible) return 0;
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

        return Recursive(this, point, results, onlySelected);
    }

    public SegmentHitResult? HitTestSegment(Vector2 point, bool onlySelected = false)
    {
        static void Recursive(SpriteNode node, Vector2 point, bool onlySelected, ref SegmentHitResult? best, ref float bestDistSqr)
        {
            if (!node.Visible) return;

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
        Recursive(this, point, onlySelected, ref best, ref bestDistSqr);
        return best;
    }

    public SpritePath? HitTestPath(Vector2 point)
    {
        static SpritePath? Recursive(SpriteNode node, Vector2 point)
        {
            if (!node.Visible) return null;

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

        return Recursive(this, point);
    }

    public int HitTestPath(Vector2 point, List<SpritePath> results)
    {
        static int Recursive(SpriteNode node, Vector2 point, List<SpritePath> results)
        {
            if (!node.Visible) return 0;
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

        return Recursive(this, point, results);
    }

    private static bool HitTestPathWithStroke(SpritePath path, Vector2 point)
    {
        if (!path.HitTestPath(point))
            return false;

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
