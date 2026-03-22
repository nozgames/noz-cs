//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class SpriteLayer
{
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public List<SpritePath> Paths { get; } = new();
    public List<SpriteLayer> Children { get; } = new();

    // Editor state (not serialized to file)
    public bool Expanded { get; set; } = true;
    public bool IsSelected { get; set; }

    public SpriteLayer Clone()
    {
        var clone = new SpriteLayer
        {
            Name = Name,
            Visible = Visible,
            Locked = Locked,
            Expanded = Expanded,
        };
        foreach (var path in Paths)
            clone.Paths.Add(path.Clone());
        foreach (var child in Children)
            clone.Children.Add(child.Clone());
        return clone;
    }

    public void CollectVisiblePaths(List<SpritePath> result)
    {
        if (!Visible)
            return;

        result.AddRange(Paths);
        foreach (var child in Children)
            child.CollectVisiblePaths(result);
    }

    public SpriteLayer? FindLayer(string name)
    {
        if (Name == name)
            return this;

        foreach (var child in Children)
        {
            var found = child.FindLayer(name);
            if (found != null)
                return found;
        }

        return null;
    }

    public SpriteLayer? FindParent(SpriteLayer target)
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

    public void ForEachLayer(Action<SpriteLayer> action)
    {
        action(this);
        foreach (var child in Children)
            child.ForEachLayer(action);
    }

    public struct LayerHitResult
    {
        public SpriteLayer Layer;
        public SpritePath Path;
        public SpritePath.HitResult Hit;
    }

    public LayerHitResult? HitTest(Vector2 point)
    {
        LayerHitResult? best = null;

        HitTestRecursive(point, ref best);
        return best;
    }

    private void HitTestRecursive(Vector2 point, ref LayerHitResult? best)
    {
        if (!Visible) return;

        // Test paths in reverse order (topmost first)
        for (var i = Paths.Count - 1; i >= 0; i--)
        {
            var path = Paths[i];
            var hit = path.HitTest(point);

            if (hit.AnchorIndex >= 0 || hit.SegmentIndex >= 0 || hit.InPath)
            {
                // Prefer anchor hits over segment hits over in-path hits
                if (!best.HasValue || IsBetterHit(hit, best.Value.Hit))
                {
                    best = new LayerHitResult
                    {
                        Layer = this,
                        Path = path,
                        Hit = hit,
                    };
                }
            }
        }

        foreach (var child in Children)
            child.HitTestRecursive(point, ref best);
    }

    private static bool IsBetterHit(SpritePath.HitResult a, SpritePath.HitResult b)
    {
        // Anchor hits beat segment hits beat in-path hits
        if (a.AnchorIndex >= 0 && b.AnchorIndex < 0) return true;
        if (a.AnchorIndex < 0 && b.AnchorIndex >= 0) return false;
        if (a.AnchorIndex >= 0) return a.AnchorDistSqr < b.AnchorDistSqr;

        if (a.SegmentIndex >= 0 && b.SegmentIndex < 0) return true;
        if (a.SegmentIndex < 0 && b.SegmentIndex >= 0) return false;
        if (a.SegmentIndex >= 0) return a.SegmentDistSqr < b.SegmentDistSqr;

        return false;
    }

    public struct PathHitResult
    {
        public SpritePath Path;
        public SpriteLayer Layer;
        public SpritePath.HitResult Hit;
    }

    public int HitTestAll(Vector2 point, List<PathHitResult> results)
    {
        if (!Visible) return 0;

        var count = 0;
        foreach (var path in Paths)
        {
            var hit = path.HitTest(point);
            if (hit.AnchorIndex >= 0 || hit.SegmentIndex >= 0 || hit.InPath)
            {
                results.Add(new PathHitResult { Path = path, Layer = this, Hit = hit });
                count++;
            }
        }

        foreach (var child in Children)
            count += child.HitTestAll(point, results);

        return count;
    }

    public void ClearAllSelections()
    {
        foreach (var path in Paths)
            path.ClearSelection();
        foreach (var child in Children)
            child.ClearAllSelections();
    }

    public SpritePath? GetPathWithSelection()
    {
        foreach (var path in Paths)
            if (path.HasSelection())
                return path;

        foreach (var child in Children)
        {
            var found = child.GetPathWithSelection();
            if (found != null)
                return found;
        }

        return null;
    }

    public SpriteLayer? FindLayerForPath(SpritePath target)
    {
        if (Paths.Contains(target))
            return this;

        foreach (var child in Children)
        {
            var found = child.FindLayerForPath(target);
            if (found != null)
                return found;
        }

        return null;
    }

    public void ForEachEditablePath(Action<SpritePath> action)
    {
        if (!Visible || Locked) return;

        foreach (var path in Paths)
            action(path);
        foreach (var child in Children)
            child.ForEachEditablePath(action);
    }

    public void SelectAnchorsInRect(Rect rect)
    {
        if (!Visible || Locked) return;

        foreach (var path in Paths)
            path.SelectAnchorsInRect(rect);
        foreach (var child in Children)
            child.SelectAnchorsInRect(rect);
    }
}
