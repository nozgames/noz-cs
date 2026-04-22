//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PixelFillMode : EditorMode<PixelSpriteEditor>
{
    public override void Update()
    {
        EditorCursor.SetCrosshair();

        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            var mouseWorld = Workspace.MouseWorldPosition;
            var pixel = Editor.WorldToPixelSnapped(mouseWorld);
            Fill(pixel);
        }
    }

    private void Fill(Vector2Int seed)
    {
        var layer = Editor.ActiveLayer;
        if (layer?.Pixels == null || layer.Locked || !layer.Visible) return;
        if (!Editor.IsPixelInConstraint(seed)) return;
        if (!Editor.IsPixelSelected(seed.X, seed.Y)) return;

        var targetColor = layer.Pixels[seed.X, seed.Y];
        var fillColor = Editor.BrushColor;

        if (targetColor == fillColor) return;

        Undo.Record(Editor.Document);

        var pixels = layer.Pixels;
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(seed);
        pixels.Set(seed.X, seed.Y, fillColor);

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();

            TryEnqueue(pixels, queue, new Vector2Int(p.X + 1, p.Y), targetColor, fillColor);
            TryEnqueue(pixels, queue, new Vector2Int(p.X - 1, p.Y), targetColor, fillColor);
            TryEnqueue(pixels, queue, new Vector2Int(p.X, p.Y + 1), targetColor, fillColor);
            TryEnqueue(pixels, queue, new Vector2Int(p.X, p.Y - 1), targetColor, fillColor);
        }

        Editor.InvalidateComposite();
        Editor.InvalidateActiveLayerPreview();
    }

    private void TryEnqueue(PixelData<Color32> pixels, Queue<Vector2Int> queue, Vector2Int p, Color32 targetColor, Color32 fillColor)
    {
        if (!Editor.IsPixelInConstraint(p)) return;
        if (!Editor.IsPixelSelected(p.X, p.Y)) return;
        if (pixels[p.X, p.Y] != targetColor) return;

        pixels.Set(p.X, p.Y, fillColor);
        queue.Enqueue(p);
    }

    public override void Draw()
    {
        var mouseWorld = Workspace.MouseWorldPosition;
        var pixel = Editor.WorldToPixelSnapped(mouseWorld);
        if (!Editor.IsPixelInBounds(pixel)) return;
        Editor.DrawBrushOutline(pixel, new Color(0.4f, 0.8f, 1f, 0.6f));
    }
}
