//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class SpriteLayer : SpriteNode
{
    public override SpriteNode Clone()
    {
        var clone = new SpriteLayer();
        ClonePropertiesTo(clone);
        foreach (var child in Children)
            clone.Add(child.Clone());
        return clone;
    }
}
