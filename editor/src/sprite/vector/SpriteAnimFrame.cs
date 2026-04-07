//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class SpriteAnimFrame
{
    public int Hold { get; set; }
    public HashSet<SpriteNode> VisibleLayers { get; } = new();

    public bool IsLayerVisible(SpriteNode node) => VisibleLayers.Contains(node);

    public void SetLayerVisible(SpriteNode node, bool visible)
    {
        if (visible)
            VisibleLayers.Add(node);
        else
            VisibleLayers.Remove(node);
    }

    public void ApplyVisibility(SpriteNode root)
    {
        root.ForEach((SpriteNode node) =>
        {
            if (node != root)
                node.Visible = VisibleLayers.Contains(node);
        });
    }

    public void CaptureVisibility(SpriteNode root)
    {
        VisibleLayers.Clear();
        root.ForEach((SpriteNode node) =>
        {
            if (node != root && node.Visible)
                VisibleLayers.Add(node);
        });
    }

    public SpriteAnimFrame Clone(SpriteNode sourceRoot, SpriteNode targetRoot)
    {
        var clone = new SpriteAnimFrame { Hold = Hold };
        MapLayers(sourceRoot, targetRoot, clone);
        return clone;
    }

    private void MapLayers(SpriteNode source, SpriteNode target, SpriteAnimFrame clone)
    {
        if (VisibleLayers.Contains(source))
            clone.VisibleLayers.Add(target);

        var minCount = Math.Min(source.Children.Count, target.Children.Count);
        for (var i = 0; i < minCount; i++)
            MapLayers(source.Children[i], target.Children[i], clone);

        // Name-based fallback for extra source layers that had no positional match
        for (var i = minCount; i < source.Children.Count; i++)
        {
            var extraSource = source.Children[i];
            if (!VisibleLayers.Contains(extraSource)) continue;

            var matched = target.FindNode(extraSource.Name);
            if (matched != null)
                clone.VisibleLayers.Add(matched);
        }
    }
}
