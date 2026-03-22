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
        root.ForEachLayer(layer =>
        {
            if (layer != root)
                layer.Visible = VisibleLayers.Contains(layer);
        });
    }

    public void CaptureVisibility(SpriteLayer root)
    {
        VisibleLayers.Clear();
        root.ForEachLayer(layer =>
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

    private void MapLayers(SpriteLayer source, SpriteLayer target, SpriteAnimFrame clone)
    {
        if (VisibleLayers.Contains(source))
            clone.VisibleLayers.Add(target);

        for (var i = 0; i < source.Children.Count && i < target.Children.Count; i++)
            MapLayers(source.Children[i], target.Children[i], clone);
    }
}
