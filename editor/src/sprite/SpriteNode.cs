//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public abstract class SpriteNode
{
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public List<SpriteNode> Children { get; } = new();

    // Editor state (not serialized to file)
    public bool Expanded { get; set; } = true;
    public bool IsSelected { get; set; }

    public abstract SpriteNode Clone();

    protected void ClonePropertiesTo(SpriteNode target)
    {
        target.Name = Name;
        target.Visible = Visible;
        target.Locked = Locked;
        target.Expanded = Expanded;
        target.IsSelected = IsSelected;
    }

    #region Tree Traversal

    public void ForEachNode(Action<SpriteNode> action)
    {
        action(this);
        foreach (var child in Children)
            child.ForEachNode(action);
    }

    public void ForEachLayer(Action<SpriteLayer> action)
    {
        if (this is SpriteLayer layer)
            action(layer);
        foreach (var child in Children)
            child.ForEachLayer(action);
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

    public SpriteLayer? FindLayer(string name)
    {
        if (this is SpriteLayer layer && layer.Name == name)
            return layer;

        foreach (var child in Children)
        {
            var found = child.FindLayer(name);
            if (found != null)
                return found;
        }

        return null;
    }

    public SpriteNode? FindParent(SpriteNode target)
    {
        foreach (var child in Children)
        {
            if (child == target)
                return this;

            var found = child.FindParent(target);
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

    public void ClearAllSelections()
    {
        if (this is SpritePath path)
            path.ClearAllSelection();

        foreach (var child in Children)
            child.ClearAllSelections();
    }

    public void ClearPathSelections()
    {
        if (this is SpritePath path)
            path.DeselectPath();

        foreach (var child in Children)
            child.ClearPathSelections();
    }

    public void ClearAnchorSelections()
    {
        if (this is SpritePath path)
            path.ClearAnchorSelection();

        foreach (var child in Children)
            child.ClearAnchorSelections();
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

    public struct NodeHitResult
    {
        public SpriteLayer Layer;
        public SpritePath Path;
        public SpritePath.HitResult Hit;
    }

    public NodeHitResult? HitTest(Vector2 point)
    {
        NodeHitResult? best = null;
        HitTestRecursive(point, ref best, this as SpriteLayer);
        return best;
    }

    private void HitTestRecursive(Vector2 point, ref NodeHitResult? best, SpriteLayer? parentLayer)
    {
        if (!Visible) return;

        var currentLayer = this is SpriteLayer layer ? layer : parentLayer;

        if (this is SpritePath path)
        {
            var hit = path.HitTest(point);
            if ((hit.AnchorIndex >= 0 || hit.SegmentIndex >= 0 || hit.InPath) && currentLayer != null)
            {
                if (!best.HasValue || IsBetterHit(hit, best.Value.Hit))
                {
                    best = new NodeHitResult
                    {
                        Layer = currentLayer,
                        Path = path,
                        Hit = hit,
                    };
                }
            }
            return;
        }

        // Iterate children in order (first child = topmost, matches render order)
        for (var i = 0; i < Children.Count; i++)
            Children[i].HitTestRecursive(point, ref best, currentLayer);
    }

    public int HitTestAll(Vector2 point, List<NodeHitResult> results)
    {
        return HitTestAllRecursive(point, results, this as SpriteLayer);
    }

    private int HitTestAllRecursive(Vector2 point, List<NodeHitResult> results, SpriteLayer? parentLayer)
    {
        if (!Visible) return 0;

        var currentLayer = this is SpriteLayer layer ? layer : parentLayer;
        var count = 0;

        if (this is SpritePath path && currentLayer != null)
        {
            var hit = path.HitTest(point);
            if (hit.AnchorIndex >= 0 || hit.SegmentIndex >= 0 || hit.InPath)
            {
                results.Add(new NodeHitResult { Path = path, Layer = currentLayer, Hit = hit });
                count++;
            }
            return count;
        }

        // Iterate children in order (first child = topmost, matches render order)
        for (var i = 0; i < Children.Count; i++)
            count += Children[i].HitTestAllRecursive(point, results, currentLayer);

        return count;
    }

    private static bool IsBetterHit(SpritePath.HitResult a, SpritePath.HitResult b)
    {
        if (a.AnchorIndex >= 0 && b.AnchorIndex < 0) return true;
        if (a.AnchorIndex < 0 && b.AnchorIndex >= 0) return false;
        if (a.AnchorIndex >= 0) return a.AnchorDistSqr < b.AnchorDistSqr;

        if (a.SegmentIndex >= 0 && b.SegmentIndex < 0) return true;
        if (a.SegmentIndex < 0 && b.SegmentIndex >= 0) return false;
        if (a.SegmentIndex >= 0) return a.SegmentDistSqr < b.SegmentDistSqr;

        return false;
    }

    #endregion
}
