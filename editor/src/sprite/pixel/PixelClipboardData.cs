//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public sealed class PixelClipboardData : IDisposable
{
    public struct LayerEntry
    {
        public string? Name;
        public PixelData<Color32> Pixels;
    }

    public LayerEntry[] Layers { get; }
    public PixelData<byte>? Mask { get; }
    public RectInt SourceRect { get; }

    public PixelClipboardData(
        IReadOnlyList<PixelLayer> layers,
        PixelData<byte>? selectionMask,
        RectInt bounds)
    {
        SourceRect = bounds;
        var entries = new List<LayerEntry>();

        foreach (var layer in layers)
        {
            if (layer.Pixels == null) continue;

            var cropped = new PixelData<Color32>(bounds.Width, bounds.Height);
            for (var y = 0; y < bounds.Height; y++)
                for (var x = 0; x < bounds.Width; x++)
                {
                    var sx = bounds.X + x;
                    var sy = bounds.Y + y;
                    if (sx < 0 || sy < 0 || sx >= layer.Pixels.Width || sy >= layer.Pixels.Height)
                        continue;
                    if (selectionMask != null && selectionMask[sx, sy] == 0)
                        continue;
                    cropped[x, y] = layer.Pixels[sx, sy];
                }

            entries.Add(new LayerEntry { Name = layer.Name, Pixels = cropped });
        }

        Layers = entries.ToArray();

        if (selectionMask != null)
        {
            Mask = new PixelData<byte>(bounds.Width, bounds.Height);
            for (var y = 0; y < bounds.Height; y++)
                for (var x = 0; x < bounds.Width; x++)
                {
                    var sx = bounds.X + x;
                    var sy = bounds.Y + y;
                    if (sx >= 0 && sy >= 0 && sx < selectionMask.Width && sy < selectionMask.Height)
                        Mask[x, y] = selectionMask[sx, sy];
                }
        }
    }

    public void Dispose()
    {
        foreach (var entry in Layers)
            entry.Pixels.Dispose();
        Mask?.Dispose();
    }
}
