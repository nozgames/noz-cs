//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public sealed class NodeClipboardData
{
    private readonly List<SpriteNode> _nodes;

    public NodeClipboardData(IReadOnlyList<SpriteLayer> layers)
    {
        _nodes = new List<SpriteNode>(layers.Count);
        foreach (var layer in layers)
        {
            var clone = layer.Clone();
            ClearSelectionRecursive(clone);
            _nodes.Add(clone);
        }
    }

    public List<SpriteNode> PasteAsNodes()
    {
        var results = new List<SpriteNode>(_nodes.Count);
        foreach (var node in _nodes)
        {
            var clone = node.Clone();
            clone.IsSelected = true;
            results.Add(clone);
        }
        return results;
    }

    private static void ClearSelectionRecursive(SpriteNode node)
    {
        node.IsSelected = false;
        if (node is SpritePath path)
            path.DeselectPath();
        foreach (var child in node.Children)
            ClearSelectionRecursive(child);
    }
}
