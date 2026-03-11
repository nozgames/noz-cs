//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class GenSpriteLayer : IDisposable
{
    public string Name = "";
    public readonly Shape Shape = new();
    public string Prompt = "";
    public string NegativePrompt = "";
    public string Seed = "";
    public bool CombineMasks;
    public int Index;

    public bool HasPrompt => !string.IsNullOrEmpty(Prompt);

    public void Dispose()
    {
        Shape.Dispose();
    }

    public GenSpriteLayer Clone()
    {
        var clone = new GenSpriteLayer
        {
            Name = Name,
            Prompt = Prompt,
            NegativePrompt = NegativePrompt,
            Seed = Seed,
            CombineMasks = CombineMasks,
            Index = Index,
        };
        clone.Shape.CopyFrom(Shape);
        return clone;
    }
}
