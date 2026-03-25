//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class SpriteAnimFrame
{
    public int Hold { get; set; }
    public HashSet<SpriteLayer> VisibleLayers { get; } = new();

    public bool IsLayerVisible(SpriteLayer layer) => VisibleLayers.Contains(layer);

    public void SetLayerVisible(SpriteLayer layer, bool visible)
    {
        if (visible)
            VisibleLayers.Add(layer);
        else
            VisibleLayers.Remove(layer);
    }

    public void ApplyVisibility(SpriteLayer root)
    {
        root.ForEach((SpriteLayer layer) =>
        {
            if (layer != root)
                layer.Visible = VisibleLayers.Contains(layer);
        });
    }

    public void CaptureVisibility(SpriteLayer root)
    {
        VisibleLayers.Clear();
        root.ForEach(layer =>
        {
            if (layer != root && layer.Visible)
                VisibleLayers.Add(layer);
        });
    }

    public SpriteAnimFrame Clone(SpriteLayer sourceRoot, SpriteLayer targetRoot)
    {
        var clone = new SpriteAnimFrame { Hold = Hold };

        // Map layers from source to target by matching tree positions
        MapLayers(sourceRoot, targetRoot, clone);
        return clone;
    }

    private void MapLayers(SpriteNode source, SpriteNode target, SpriteAnimFrame clone)
    {
        if (source is SpriteLayer sourceLayer && target is SpriteLayer targetLayer)
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
            if (source.Children[i] is not SpriteLayer extraSource) continue;
            if (!VisibleLayers.Contains(extraSource)) continue;

            var matched = target.FindLayer(extraSource.Name);
            if (matched != null)
                clone.VisibleLayers.Add(matched);
        }
    }
}
