//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class SpriteGroup : SpriteNode
{
    public override bool IsExpandable => true;
    public override SpriteNode Clone()
    {
        var clone = new SpriteGroup();
        ClonePropertiesTo(clone);
        foreach (var child in Children)
            clone.Add(child.Clone());
        return clone;
    }
}
