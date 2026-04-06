//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class SpriteAnimFrame
{
    public int Hold { get; set; }
    public HashSet<SpriteGroup> VisibleLayers { get; } = new();

    public bool IsLayerVisible(SpriteGroup layer) => VisibleLayers.Contains(layer);

    public void SetLayerVisible(SpriteGroup layer, bool visible)
    {
        if (visible)
            VisibleLayers.Add(layer);
        else
            VisibleLayers.Remove(layer);
    }

    public void ApplyVisibility(SpriteGroup root)
    {
        root.ForEach((SpriteGroup layer) =>
        {
            if (layer != root)
                layer.Visible = VisibleLayers.Contains(layer);
        });
    }

    public void CaptureVisibility(SpriteGroup root)
    {
        VisibleLayers.Clear();
        root.ForEach(layer =>
        {
            if (layer != root && layer.Visible)
                VisibleLayers.Add(layer);
        });
    }

    public SpriteAnimFrame Clone(SpriteGroup sourceRoot, SpriteGroup targetRoot)
    {
        var clone = new SpriteAnimFrame { Hold = Hold };

        // Map layers from source to target by matching tree positions
        MapLayers(sourceRoot, targetRoot, clone);
        return clone;
    }

    private void MapLayers(SpriteNode source, SpriteNode target, SpriteAnimFrame clone)
    {
        if (source is SpriteGroup sourceLayer && target is SpriteGroup targetLayer)
        {
            if (VisibleLayers.Contains(sourceLayer))
                clone.VisibleLayers.Add(targetLayer);
        }

        // Map children by position, then fall back to name matching for unmatched layers
        var minCount = Math.Min(source.Children.Count, target.Children.Count);
        for (var i = 0; i < minCount; i++)
            MapLayers(source.Children[i], target.Children[i], clone);

        // Name-based fallback for extra source layers that had no positional match
        for (var i = minCount; i < source.Children.Count; i++)
        {
            if (source.Children[i] is not SpriteGroup extraSource) continue;
            if (!VisibleLayers.Contains(extraSource)) continue;

            var matched = target.FindGroup(extraSource.Name);
            if (matched != null)
                clone.VisibleLayers.Add(matched);
        }
    }
}
