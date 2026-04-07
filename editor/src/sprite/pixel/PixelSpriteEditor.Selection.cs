//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public enum SelectionOp { Replace, Add, Subtract }

public partial class PixelSpriteEditor
{
    public bool HasSelection => Document.SelectionMask != null;

    public bool IsPixelSelected(int x, int y) =>
        Document.SelectionMask == null || Document.SelectionMask[x, y] > 0;

    public void ClearSelection()
    {
        Document.SelectionMask?.Dispose();
        Document.SelectionMask = null;
    }

    public void SelectAll()
    {
        var w = Document.CanvasSize.X;
        var h = Document.CanvasSize.Y;
        Document.SelectionMask?.Dispose();
        Document.SelectionMask = new PixelData<byte>(w, h);
        for (var i = 0; i < w * h; i++)
            Document.SelectionMask[i] = 255;
    }

    public void InvertSelection()
    {
        if (Document.SelectionMask == null)
            return;

        var total = Document.CanvasSize.X * Document.CanvasSize.Y;
        for (var i = 0; i < total; i++)
            Document.SelectionMask[i] = (byte)(255 - Document.SelectionMask[i]);
    }

    public void DeleteSelected()
    {
        var layer = ActiveLayer;
        if (layer?.Pixels == null) return;

        Undo.Record(Document);
        var w = Document.CanvasSize.X;
        var h = Document.CanvasSize.Y;

        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (IsPixelSelected(x, y))
                    layer.Pixels.Set(x, y, default);

        InvalidateComposite();
    }

    public void ApplyRectSelection(RectInt rect, SelectionOp op)
    {
        var w = Document.CanvasSize.X;
        var h = Document.CanvasSize.Y;

        // Clamp rect to canvas
        var x0 = Math.Max(0, rect.X);
        var y0 = Math.Max(0, rect.Y);
        var x1 = Math.Min(w, rect.X + rect.Width);
        var y1 = Math.Min(h, rect.Y + rect.Height);

        if (op == SelectionOp.Replace)
        {
            Document.SelectionMask?.Dispose();
            Document.SelectionMask = new PixelData<byte>(w, h);
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                    Document.SelectionMask[x, y] = 255;
        }
        else if (op == SelectionOp.Add)
        {
            Document.SelectionMask ??= new PixelData<byte>(w, h);
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                    Document.SelectionMask[x, y] = 255;
        }
        else if (op == SelectionOp.Subtract)
        {
            if (Document.SelectionMask == null) return;
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                    Document.SelectionMask[x, y] = 0;
        }
    }

    public RectInt? GetSelectionBounds()
    {
        if (Document.SelectionMask == null) return null;

        var w = Document.CanvasSize.X;
        var h = Document.CanvasSize.Y;
        var minX = w; var minY = h;
        var maxX = 0; var maxY = 0;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (Document.SelectionMask[x, y] <= 0) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x >= maxX) maxX = x + 1;
                if (y >= maxY) maxY = y + 1;
            }
        }

        if (minX >= maxX) return null;
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    public void DrawSelectionRect(Rect selRect)
    {
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetColor(new Color(0f, 0f, 0f, 0.6f));
            Gizmos.DrawRect(selRect, EditorStyle.Workspace.DocumentBoundsLineWidth * 2f);
            Graphics.SetColor(new Color(1f, 1f, 1f, 0.8f));
            Gizmos.DrawRect(selRect, EditorStyle.Workspace.DocumentBoundsLineWidth);
        }
    }

    private void DrawSelectionOutline()
    {
        var selBounds = GetSelectionBounds();
        if (selBounds == null) return;

        var sb = selBounds.Value;
        var canvas = CanvasRect;
        var epr = EditablePixelRect;
        var cellW = canvas.Width / epr.Width;
        var cellH = canvas.Height / epr.Height;

        var selRect = new Rect(
            canvas.X + (sb.X - epr.X) * cellW,
            canvas.Y + (sb.Y - epr.Y) * cellH,
            sb.Width * cellW,
            sb.Height * cellH);

        DrawSelectionRect(selRect);
    }
}
