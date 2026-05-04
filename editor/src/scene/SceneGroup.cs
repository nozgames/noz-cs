//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class SceneGroup : SceneNode
{
    public override bool IsExpandable => true;

    public override SceneNode Clone()
    {
        var clone = new SceneGroup();
        ClonePropertiesTo(clone);
        foreach (var child in Children)
            clone.Add(child.Clone());
        return clone;
    }
}
