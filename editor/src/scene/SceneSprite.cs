//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class SceneSprite : SceneNode
{
    public DocumentRef<SpriteDocument> Sprite;

    public override SceneNode Clone()
    {
        var clone = new SceneSprite { Sprite = Sprite };
        ClonePropertiesTo(clone);
        return clone;
    }
}
