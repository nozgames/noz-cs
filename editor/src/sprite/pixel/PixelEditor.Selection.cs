//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public enum SelectionOp { Replace, Add, Subtract }

public partial class PixelEditor
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
        InvalidateActiveLayerPreview();
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

    private readonly List<PixelLayer> _selectedLayers = [];

    public List<PixelLayer> GetSelectedLayers()
    {
        _selectedLayers.Clear();
        CollectSelectedLayers(Document.Root);

        // Fallback: nothing selected → use active layer
        if (_selectedLayers.Count == 0 && ActiveLayer is { Pixels: not null } active)
            _selectedLayers.Add(active);

        return _selectedLayers;
    }

    private void CollectSelectedLayers(SpriteNode node)
    {
        if (node.IsSelected)
        {
            if (node.IsExpandable)
            {
                node.Collect(_selectedLayers, layer => layer.Pixels != null);
                return;
            }

            if (node is PixelLayer { Pixels: not null } layer)
            {
                _selectedLayers.Add(layer);
                return;
            }
        }

        foreach (var child in node.Children)
            CollectSelectedLayers(child);
    }

    public RectInt? GetLayerContentBounds(IReadOnlyList<PixelLayer> layers)
    {
        var w = Document.CanvasSize.X;
        var h = Document.CanvasSize.Y;
        var minX = w; var minY = h;
        var maxX = 0; var maxY = 0;
        var hasContent = false;

        foreach (var layer in layers)
        {
            if (layer.Pixels == null) continue;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (!IsPixelInBounds(new Vector2Int(x, y))) continue;
                    if (layer.Pixels[x, y].A == 0) continue;

                    hasContent = true;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x >= maxX) maxX = x + 1;
                    if (y >= maxY) maxY = y + 1;
                }
        }

        if (!hasContent) return null;
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    public void FitSelectionToContent()
    {
        if (Document.SelectionMask == null) return;

        var sb = GetSelectionBounds();
        if (sb == null) { ClearSelection(); return; }

        var s = sb.Value;
        var minX = s.X + s.Width;
        var minY = s.Y + s.Height;
        var maxX = s.X;
        var maxY = s.Y;

        var hasContent = false;
        var layers = GetSelectedLayers();
        foreach (var layer in layers)
        {
            if (layer.Pixels == null) continue;
            for (var y = s.Y; y < s.Y + s.Height; y++)
                for (var x = s.X; x < s.X + s.Width; x++)
                {
                    if (!IsPixelSelected(x, y)) continue;
                    if (!IsPixelInBounds(new Vector2Int(x, y))) continue;
                    if (layer.Pixels[x, y].A == 0) continue;

                    hasContent = true;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x >= maxX) maxX = x + 1;
                    if (y >= maxY) maxY = y + 1;
                }
        }

        if (!hasContent) { ClearSelection(); return; }

        ApplyRectSelection(new RectInt(minX, minY, maxX - minX, maxY - minY), SelectionOp.Replace);
    }

    private void CopySelected()
    {
        if (!HasSelection)
        {
            var layers = GetSelectedLayers();
            if (layers.Count == 0) return;
            Clipboard.Copy(new SpriteClipboardData(layers));
            return;
        }

        var bounds = GetSelectionBounds();
        if (bounds == null) return;

        var selectedLayers = GetSelectedLayers();
        if (selectedLayers.Count == 0) return;

        Clipboard.Copy(new PixelClipboardData(selectedLayers, Document.SelectionMask, bounds.Value));
    }

    private void PasteSelected()
    {
        var nodeData = Clipboard.Get<SpriteClipboardData>();
        if (nodeData != null)
        {
            Undo.Record(Document);
            var nodes = nodeData.PasteAsNodes();
            foreach (var node in nodes)
                Document.Root.Insert(0, node);
            InvalidateComposite();
            return;
        }

        var pixelData = Clipboard.Get<PixelClipboardData>();
        if (pixelData == null) return;

        var targetLayer = ActiveLayer;
        if (targetLayer?.Pixels == null) return;

        Undo.Record(Document);

        var src = pixelData.SourceRect;
        var entry = pixelData.Layers[0];
        for (var y = 0; y < src.Height; y++)
            for (var x = 0; x < src.Width; x++)
            {
                if (pixelData.Mask != null && pixelData.Mask[x, y] == 0) continue;
                var c = entry.Pixels[x, y];
                if (c.A == 0) continue;
                var dx = src.X + x;
                var dy = src.Y + y;
                if (IsPixelInBounds(new Vector2Int(dx, dy)))
                    targetLayer.Pixels.Set(dx, dy, c);
            }

        ApplyRectSelection(src, SelectionOp.Replace);
        InvalidateComposite();
        InvalidateActiveLayerPreview();
        SetMode(new PixelTransformMode());
    }

    private void CutSelected()
    {
        if (!HasSelection && GetSelectedLayers().Count == 0) return;
        CopySelected();

        if (HasSelection)
            DeleteSelected();
        else
            DeleteActiveLayer();
    }

    private void DuplicateSelected()
    {
        if (!HasSelection)
        {
            var layers = GetSelectedLayers();
            if (layers.Count == 0) return;

            Undo.Record(Document);
            foreach (var layer in layers)
            {
                var clone = layer.Clone();
                clone.IsSelected = true;
                layer.IsSelected = false;
                layer.Parent?.Insert(0, clone);
            }
            InvalidateComposite();
            return;
        }

        CopySelected();
        PasteSelected();
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
