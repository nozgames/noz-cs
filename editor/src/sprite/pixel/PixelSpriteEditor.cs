//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum SelectionOp { Replace, Add, Subtract }

public partial class PixelSpriteEditor : DocumentEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId PencilButton { get; }
        public static partial WidgetId EraserButton { get; }
        public static partial WidgetId ColorButton { get; }
        public static partial WidgetId EyeDropperButton { get; }
        public static partial WidgetId RectSelectButton { get; }
        public static partial WidgetId MoveButton { get; }
    }

    public new SpriteDocument Document => (SpriteDocument)base.Document;

    public Color32 BrushColor { get; set; } = Color32.Black;
    public int BrushSize { get; set; } = 1;
    public SpriteLayer? ActiveLayer { get; set; }

    public bool HasSelection => Document.SelectionMask != null;

    private Texture? _canvasTexture;
    private PixelData<Color32>? _compositePixels;
    private int _lastCompositeVersion = -1;
    private readonly int _versionOnOpen;

    public override bool ShowInspector => true;
    public override bool ShowOutliner => true;

    public PixelSpriteEditor(SpriteDocument document) : base(document)
    {
        _versionOnOpen = document.Version;

        Commands =
        [
            new Command("Exit Edit Mode", Workspace.EndEdit, [InputCode.KeyTab]),
            new Command("Pencil", () => SetMode(new PencilMode()), [new KeyBinding(InputCode.KeyB)]),
            new Command("Eraser", () => SetMode(new PixelEraserMode()), [new KeyBinding(InputCode.KeyE)]),
            new Command("Rect Select", () => SetMode(new PixelRectSelectMode()), [new KeyBinding(InputCode.KeyM)]),
            new Command("Move", () => SetMode(new PixelMoveMode()), [new KeyBinding(InputCode.KeyV)]),
            new Command("Eye Dropper", () => SetMode(new PixelEyeDropperMode()), [new KeyBinding(InputCode.KeyI)]),
            new Command("Select All", SelectAll, [new KeyBinding(InputCode.KeyA, ctrl: true)]),
            new Command("Deselect", ClearSelection, [new KeyBinding(InputCode.KeyD, ctrl: true)]),
            new Command("Invert Selection", InvertSelection, [new KeyBinding(InputCode.KeyI, ctrl: true, shift: true)]),
            new Command("Delete Selected", DeleteSelected, [InputCode.KeyDelete]),
            new Command("Increase Brush", () => BrushSize = Math.Min(BrushSize + 1, 16), [InputCode.KeyRightBracket]),
            new Command("Decrease Brush", () => BrushSize = Math.Max(BrushSize - 1, 1), [InputCode.KeyLeftBracket]),
        ];

        // Ensure at least one layer exists
        if (Document.RootLayer.Children.Count == 0)
            AddLayer();

        // Ensure all layers have pixel data allocated
        foreach (var child in Document.RootLayer.Children)
        {
            if (child is SpriteLayer layer && layer.Pixels == null)
                layer.Pixels = new PixelData<Color32>(Document.CanvasSize.X, Document.CanvasSize.Y);
        }

        ActiveLayer = FindFirstLayer();
        BrushSize = Document.PixelBrushSize;
        BrushColor = Document.PixelBrushColor;
        SetMode(new PencilMode());
    }

    private SpriteLayer? FindFirstLayer()
    {
        foreach (var child in Document.RootLayer.Children)
            if (child is SpriteLayer layer)
                return layer;
        return null;
    }

    public void AddLayer()
    {
        Undo.Record(Document);
        var layer = new SpriteLayer
        {
            Name = $"Layer {Document.RootLayer.Children.Count + 1}",
            Pixels = new PixelData<Color32>(Document.CanvasSize.X, Document.CanvasSize.Y)
        };
        Document.RootLayer.Add(layer);
        ActiveLayer = layer;
    }

    public override void Update()
    {
        CompositeCanvas();

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(4);
            Document.DrawOrigin();
        }

        DrawCanvas();
        DrawPixelGrid();
        if (Mode is not PixelMoveMode)
            DrawSelectionOutline();
        Document.DrawBounds();
        Mode?.Draw();
    }

    public override void LateUpdate()
    {
        Mode?.Update();
    }

    public override void UpdateUI() { }

    public override void UpdateOverlayUI()
    {
        using (FloatingToolbar.Begin())
        {
            if (FloatingToolbar.Button(WidgetIds.PencilButton, EditorAssets.Sprites.IconEdit, isSelected: Mode is PencilMode))
                SetMode(new PencilMode());

            if (FloatingToolbar.Button(WidgetIds.EraserButton, EditorAssets.Sprites.IconDelete, isSelected: Mode is PixelEraserMode))
                SetMode(new PixelEraserMode());

            FloatingToolbar.Divider();

            if (FloatingToolbar.Button(WidgetIds.RectSelectButton, EditorAssets.Sprites.IconClip, isSelected: Mode is PixelRectSelectMode))
                SetMode(new PixelRectSelectMode());

            if (FloatingToolbar.Button(WidgetIds.MoveButton, EditorAssets.Sprites.IconMove, isSelected: Mode is PixelMoveMode))
                SetMode(new PixelMoveMode());

            FloatingToolbar.Divider();

            if (FloatingToolbar.Button(WidgetIds.EyeDropperButton, EditorAssets.Sprites.IconPreview, isSelected: Mode is PixelEyeDropperMode))
                SetMode(new PixelEyeDropperMode());
        }
    }

    private void CompositeCanvas()
    {
        if (_lastCompositeVersion == Document.Version)
            return;

        _lastCompositeVersion = Document.Version;

        var w = Document.CanvasSize.X;
        var h = Document.CanvasSize.Y;

        _compositePixels ??= new PixelData<Color32>(w, h);
        _compositePixels.Clear();

        foreach (var child in Document.RootLayer.Children)
        {
            if (child is not SpriteLayer layer || !layer.Visible || layer.Pixels == null)
                continue;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var src = layer.Pixels[x, y];
                    if (src.A == 0) continue;

                    ref var dst = ref _compositePixels[x, y];
                    if (dst.A == 0)
                    {
                        dst = src;
                    }
                    else
                    {
                        // Alpha blend: src over dst
                        var sa = src.A / 255f;
                        var da = dst.A / 255f;
                        var outA = sa + da * (1f - sa);
                        if (outA > 0f)
                        {
                            var invOutA = 1f / outA;
                            dst = new Color32(
                                (byte)((src.R * sa + dst.R * da * (1f - sa)) * invOutA),
                                (byte)((src.G * sa + dst.G * da * (1f - sa)) * invOutA),
                                (byte)((src.B * sa + dst.B * da * (1f - sa)) * invOutA),
                                (byte)(outA * 255f));
                        }
                    }
                }
            }
        }

        // Upload to GPU
        var data = _compositePixels.AsByteSpan();
        if (_canvasTexture == null)
            _canvasTexture = Texture.Create(w, h, data, TextureFormat.RGBA8, TextureFilter.Point, "pixel_canvas");
        else
            _canvasTexture.Update(data);
    }

    private void DrawCanvas()
    {
        if (_canvasTexture == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetTexture(_canvasTexture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTextureFilter(TextureFilter.Point);
            Graphics.SetColor(Color.White);
            Graphics.Draw(Document.Bounds, order: Document.SortOrder);
        }
    }

    private void DrawPixelGrid()
    {
        // Only draw grid when zoomed in enough
        var pixelWorldSize = Document.Bounds.Width / Document.CanvasSize.X;
        var pixelScreenSize = pixelWorldSize * Workspace.Zoom;
        if (pixelScreenSize < 4f) return;

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetColor(new Color(0.5f, 0.5f, 0.5f, 0.15f));
            Graphics.SetSortGroup(5);

            var bounds = Document.Bounds;
            var w = Document.CanvasSize.X;
            var h = Document.CanvasSize.Y;
            var cellW = bounds.Width / w;
            var cellH = bounds.Height / h;

            // Vertical lines
            for (var x = 0; x <= w; x++)
            {
                var xPos = bounds.X + x * cellW;
                Gizmos.DrawLine(
                    new Vector2(xPos, bounds.Y),
                    new Vector2(xPos, bounds.Bottom),
                    EditorStyle.Workspace.DocumentBoundsLineWidth);
            }

            // Horizontal lines
            for (var y = 0; y <= h; y++)
            {
                var yPos = bounds.Y + y * cellH;
                Gizmos.DrawLine(
                    new Vector2(bounds.X, yPos),
                    new Vector2(bounds.Right, yPos),
                    EditorStyle.Workspace.DocumentBoundsLineWidth);
            }
        }
    }

    public Vector2Int WorldToPixel(Vector2 worldPos)
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var local = Vector2.Transform(worldPos, invTransform);
        var bounds = Document.Bounds;
        var nx = (local.X - bounds.X) / bounds.Width;
        var ny = (local.Y - bounds.Y) / bounds.Height;
        return new Vector2Int(
            (int)MathF.Floor(nx * Document.CanvasSize.X),
            (int)MathF.Floor(ny * Document.CanvasSize.Y));
    }

    public bool IsPixelInBounds(Vector2Int pixel) =>
        pixel.X >= 0 && pixel.X < Document.CanvasSize.X &&
        pixel.Y >= 0 && pixel.Y < Document.CanvasSize.Y;

    public void InvalidateComposite()
    {
        _lastCompositeVersion = -1;
    }

    // --- Selection ---

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
        {
            // No selection = everything selected; invert = nothing selected (clear)
            // But more useful: treat no selection as "all selected", invert to empty
            return;
        }

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

    private void DrawSelectionOutline()
    {
        var selBounds = GetSelectionBounds();
        if (selBounds == null) return;

        var sb = selBounds.Value;
        var bounds = Document.Bounds;
        var cellW = bounds.Width / Document.CanvasSize.X;
        var cellH = bounds.Height / Document.CanvasSize.Y;

        var selRect = new Rect(
            bounds.X + sb.X * cellW,
            bounds.Y + sb.Y * cellH,
            sb.Width * cellW,
            sb.Height * cellH);

        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(Document.Transform);

            // Draw black outline then white outline for contrast
            Graphics.SetColor(new Color(0f, 0f, 0f, 0.8f));
            Gizmos.DrawRect(selRect, EditorStyle.Workspace.DocumentBoundsLineWidth * 2f);
            Graphics.SetColor(new Color(1f, 1f, 1f, 0.9f));
            Gizmos.DrawRect(selRect, EditorStyle.Workspace.DocumentBoundsLineWidth);
        }
    }

    public override void OnUndoRedo()
    {
        InvalidateComposite();
        ActiveLayer ??= FindFirstLayer();
        base.OnUndoRedo();
    }

    public static bool IsInBrush(int dx, int dy, int brushSize)
    {
        if (brushSize <= 2) return true;
        var center = (brushSize - 1) / 2.0f;
        var distX = dx - center;
        var distY = dy - center;
        var radius = brushSize / 2.0f;
        return distX * distX + distY * distY <= radius * radius;
    }

    public void PaintBrush(Vector2Int pixel, Color32 color)
    {
        var layer = ActiveLayer;
        if (layer?.Pixels == null) return;

        var offset = (BrushSize - 1) / 2;
        for (var dy = 0; dy < BrushSize; dy++)
            for (var dx = 0; dx < BrushSize; dx++)
            {
                if (!IsInBrush(dx, dy, BrushSize)) continue;
                var px = pixel.X - offset + dx;
                var py = pixel.Y - offset + dy;
                if (!IsPixelInBounds(new Vector2Int(px, py))) continue;
                if (!IsPixelSelected(px, py)) continue;
                layer.Pixels.Set(px, py, color);
            }
        InvalidateComposite();
    }

    public void DrawBrushOutline(Vector2Int pixel, Color color)
    {
        var bounds = Document.Bounds;
        var cellW = bounds.Width / Document.CanvasSize.X;
        var cellH = bounds.Height / Document.CanvasSize.Y;
        var offset = (BrushSize - 1) / 2;
        var lineWidth = EditorStyle.Workspace.DocumentBoundsLineWidth;

        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(6);
            Graphics.SetColor(color);

            for (var dy = 0; dy < BrushSize; dy++)
                for (var dx = 0; dx < BrushSize; dx++)
                {
                    if (!IsInBrush(dx, dy, BrushSize)) continue;
                    var px = pixel.X - offset + dx;
                    var py = pixel.Y - offset + dy;
                    if (!IsPixelInBounds(new Vector2Int(px, py))) continue;

                    var x0 = bounds.X + px * cellW;
                    var y0 = bounds.Y + py * cellH;
                    var x1 = x0 + cellW;
                    var y1 = y0 + cellH;

                    // Draw edge if neighbor is not in brush or out of canvas
                    if (!IsBrushNeighbor(dx, dy - 1, px, py - 1))
                        Gizmos.DrawLine(new Vector2(x0, y0), new Vector2(x1, y0), lineWidth);
                    if (!IsBrushNeighbor(dx, dy + 1, px, py + 1))
                        Gizmos.DrawLine(new Vector2(x0, y1), new Vector2(x1, y1), lineWidth);
                    if (!IsBrushNeighbor(dx - 1, dy, px - 1, py))
                        Gizmos.DrawLine(new Vector2(x0, y0), new Vector2(x0, y1), lineWidth);
                    if (!IsBrushNeighbor(dx + 1, dy, px + 1, py))
                        Gizmos.DrawLine(new Vector2(x1, y0), new Vector2(x1, y1), lineWidth);
                }
        }

        bool IsBrushNeighbor(int ndx, int ndy, int npx, int npy) =>
            ndx >= 0 && ndx < BrushSize && ndy >= 0 && ndy < BrushSize &&
            IsInBrush(ndx, ndy, BrushSize) && IsPixelInBounds(new Vector2Int(npx, npy));
    }

    public override void Dispose()
    {
        Document.PixelBrushSize = BrushSize;
        Document.PixelBrushColor = BrushColor;

        if (Document.Version != _versionOnOpen && Document.Atlas != null)
            AtlasManager.UpdateSource(Document);

        _canvasTexture?.Dispose();
        _compositePixels?.Dispose();
        base.Dispose();
    }
}
