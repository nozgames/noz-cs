//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public sealed class SceneClipboardData
{
    public SceneNode[] Roots { get; }

    public SceneClipboardData(IReadOnlyList<SceneNode> nodes)
    {
        Roots = new SceneNode[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            var clone = nodes[i].Clone();
            clone.ForEach(n => n.IsSelected = false);
            Roots[i] = clone;
        }
    }

    public List<SceneNode> PasteAsNodes()
    {
        var result = new List<SceneNode>(Roots.Length);
        foreach (var root in Roots)
        {
            var clone = root.Clone();
            clone.ForEach(n => n.IsSelected = false);
            result.Add(clone);
        }
        return result;
    }
}
