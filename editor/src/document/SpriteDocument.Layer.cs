//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class GenerationConfig
{
    public string Prompt = "";
    public string NegativePrompt = "";
    public string Style = "";
    public long Seed;
    public float ControlNetStrength = 0.3f;
    public float StyleStrength = 0.7f;
    public bool Auto;

    // Editor-only state (not persisted)
    public Texture? GeneratedTexture;
    public bool IsGenerating;

    public bool HasPrompt => !string.IsNullOrEmpty(Prompt);

    public GenerationConfig Clone() => new()
    {
        Prompt = Prompt,
        NegativePrompt = NegativePrompt,
        Style = Style,
        Seed = Seed,
        ControlNetStrength = ControlNetStrength,
        StyleStrength = StyleStrength,
        Auto = Auto,
    };
}

public class SpriteFrame : IDisposable
{
    public readonly Shape Shape = new();
    public int Hold;

    public void Dispose()
    {
        Shape.Dispose();
    }
}

public class SpriteLayer
{
    public string Name = "";
    public bool Visible = true;
    public bool Locked;
    public float Opacity = 1.0f;
    public byte SortOrder;
    public StringId Bone;
    public GenerationConfig? Generation;
    public int Index;

    public readonly SpriteFrame[] Frames = new SpriteFrame[Sprite.MaxFrames];
    public ushort FrameCount = 1;

    public bool IsGenerated => Generation != null;
    public bool HasGeneration => Generation is { HasPrompt: true };

    public int TotalTimeSlots
    {
        get
        {
            var total = 0;
            for (var i = 0; i < FrameCount; i++)
                total += 1 + Frames[i].Hold;
            return total;
        }
    }

    public SpriteLayer()
    {
        for (var i = 0; i < Frames.Length; i++)
            Frames[i] = new SpriteFrame();
    }

    public int InsertFrame(int insertAt)
    {
        if (FrameCount >= Sprite.MaxFrames)
            return -1;

        FrameCount++;
        var copyFrame = Math.Max(0, insertAt - 1);

        for (var i = FrameCount - 1; i > insertAt; i--)
        {
            Frames[i].Shape.CopyFrom(Frames[i - 1].Shape);
            Frames[i].Hold = Frames[i - 1].Hold;
        }

        if (copyFrame >= 0 && copyFrame < FrameCount)
            Frames[insertAt].Shape.CopyFrom(Frames[copyFrame].Shape);

        Frames[insertAt].Hold = 0;
        return insertAt;
    }

    public int DeleteFrame(int frameIndex)
    {
        if (FrameCount <= 1)
            return frameIndex;

        for (var i = frameIndex; i < FrameCount - 1; i++)
        {
            Frames[i].Shape.CopyFrom(Frames[i + 1].Shape);
            Frames[i].Hold = Frames[i + 1].Hold;
        }

        Frames[FrameCount - 1].Shape.Clear();
        Frames[FrameCount - 1].Hold = 0;
        FrameCount--;
        return Math.Min(frameIndex, FrameCount - 1);
    }

    public SpriteLayer Clone()
    {
        var clone = new SpriteLayer
        {
            Name = Name,
            Visible = Visible,
            Locked = Locked,
            Opacity = Opacity,
            SortOrder = SortOrder,
            Index = Index,
            Bone = Bone,
            Generation = Generation?.Clone(),
            FrameCount = FrameCount,
        };
        for (var i = 0; i < FrameCount; i++)
        {
            clone.Frames[i].Shape.CopyFrom(Frames[i].Shape);
            clone.Frames[i].Hold = Frames[i].Hold;
        }
        return clone;
    }
}

public partial class SpriteDocument
{
    public bool IsLayerActive(SpriteLayer layer) => ActiveLayerIndex == layer.Index;

    public int AddLayer()
    {
        if (_layers.Count >= MaxDocumentLayers)
            return -1;

        var name = $"Layer {_layers.Count + 1}";

        _layers.Add(new SpriteLayer { Name = name, Index = _layers.Count });
        ActiveLayerIndex = _layers.Count - 1;
        return ActiveLayerIndex;
    }

    public void RemoveLayer(int index)
    {
        if (index < 0 || index >= _layers.Count || _layers.Count <= 1)
            return;

        _layers.RemoveAt(index);

        for (var i=index; i < _layers.Count; i++)
            _layers[i].Index = i;

        if (ActiveLayerIndex >= _layers.Count)
            ActiveLayerIndex = _layers.Count - 1;

        UpdateBounds();
    }

    public void MoveLayer(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _layers.Count ||
            toIndex < 0 || toIndex >= _layers.Count ||
            fromIndex == toIndex)
            return;

        var layer = _layers[fromIndex];
        _layers.RemoveAt(fromIndex);
        _layers.Insert(toIndex, layer);

        for (var i = 0; i < _layers.Count; i++)
            _layers[i].Index = i;

        if (ActiveLayerIndex == fromIndex)
            ActiveLayerIndex = toIndex;
        else if (fromIndex < toIndex && ActiveLayerIndex > fromIndex && ActiveLayerIndex <= toIndex)
            ActiveLayerIndex--;
        else if (fromIndex > toIndex && ActiveLayerIndex >= toIndex && ActiveLayerIndex < fromIndex)
            ActiveLayerIndex++;

        UpdateBounds();
    }
}
