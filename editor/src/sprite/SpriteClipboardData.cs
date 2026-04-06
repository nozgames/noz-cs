//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public sealed class SpriteClipboardData
{
    private readonly List<SpriteNode> _nodes;

    public SpriteClipboardData(IReadOnlyList<SpriteNode> nodes)
    {
        _nodes = new List<SpriteNode>(nodes.Count);
        foreach (var node in nodes)
        {
            var clone = node.Clone();
            clone.ClearSelection();
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
}
