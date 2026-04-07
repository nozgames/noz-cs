//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class PixelLayer : SpriteNode
{
    public PixelData<Color32>? Pixels { get; set; }

    public override SpriteNode Clone()
    {
        var clone = new PixelLayer();
        ClonePropertiesTo(clone);

        if (Pixels != null)
            clone.Pixels = Pixels.Clone();

        return clone;
    }

    public override void Dispose()
    {
        base.Dispose();
        Pixels?.Dispose();
        Pixels = null;
    }
}
