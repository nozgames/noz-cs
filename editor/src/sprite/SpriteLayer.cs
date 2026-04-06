//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class SpriteLayer : SpriteNode
{
    public PixelData<Color32>? Pixels { get; set; }

    public override SpriteNode Clone()
    {
        var clone = new SpriteLayer();
        ClonePropertiesTo(clone);

        if (Pixels != null)
        {
            var src = Pixels;
            var dst = new PixelData<Color32>(src.Width, src.Height);
            for (var i = 0; i < src.Width * src.Height; i++)
                dst[i] = src[i];
            clone.Pixels = dst;
        }

        foreach (var child in Children)
            clone.Add(child.Clone());
        return clone;
    }
}
