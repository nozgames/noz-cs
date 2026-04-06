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

    private SpriteLayer? _activeLayer;
    public SpriteLayer? ActiveLayer
    {
        get => _activeLayer;
        set => _activeLayer = value?.Pixels != null ? value : _activeLayer;
    }

    public bool HasSelection => Document.SelectionMask != null;

    private Texture? _canvasTexture;
    private PixelData<Color32>? _compositePixels;
    private int _lastCompositeVersion = -1;
    private readonly int _versionOnOpen;

    private const float CanvasPPU = 32f;

    public Rect CanvasRect
    {
        get
        {
            var w = Document.CanvasSize.X / CanvasPPU;
            var h = Document.CanvasSize.Y / CanvasPPU;
            return new Rect(-w / 2, -h / 2, w, h);
        }
    }

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
            new Command("Delete", Delete, [InputCode.KeyX, InputCode.KeyDelete]),
            new Command("Increase Brush", () => BrushSize = Math.Min(BrushSize + 1, 16), [InputCode.KeyRightBracket]),
            new Command("Decrease Brush", () => BrushSize = Math.Max(BrushSize - 1, 1), [InputCode.KeyLeftBracket]),
            new Command("Rename", BeginRename, [InputCode.KeyF2]),
        ];

        // Ensure at least one layer exists
        if (Document.RootLayer.Children.Count == 0)
            AddLayer();

        // Ensure all leaf layers have pixel data allocated
        InitializeLayerPixels(Document.RootLayer);

        ActiveLayer = FindFirstLayer();
        BrushSize = Document.PixelBrushSize;
        BrushColor = Document.PixelBrushColor;
        Grid.PixelsPerUnitOverride = CanvasPPU;
        SetMode(new PencilMode());
    }

    private void InitializeLayerPixels(SpriteNode parent)
    {
        foreach (var child in parent.Children)
        {
            if (child is not SpriteLayer layer)
                continue;

            if (layer.Children.Count > 0)
            {
                // Group — recurse but don't allocate pixels
                InitializeLayerPixels(layer);
            }
            else if (layer.Pixels == null)
            {
                layer.Pixels = new PixelData<Color32>(Document.CanvasSize.X, Document.CanvasSize.Y);
            }
        }
    }

    private SpriteLayer? FindFirstLayer()
    {
        return FindFirstLeafLayer(Document.RootLayer);
    }

    private static SpriteLayer? FindFirstLeafLayer(SpriteNode parent)
    {
        foreach (var child in parent.Children)
        {
            if (child is not SpriteLayer layer)
                continue;

            if (layer.Pixels != null)
                return layer;

            var found = FindFirstLeafLayer(layer);
            if (found != null)
                return found;
        }
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

    public void AddGroup()
    {
        Undo.Record(Document);
        var group = new SpriteLayer
        {
            Name = $"Group {Document.RootLayer.Children.Count + 1}",
        };
        Document.RootLayer.Add(group);
    }

    public void DeleteActiveLayer()
    {
        if (_activeLayer == null) return;

        // Don't delete the last leaf layer
        if (FindFirstLeafLayer(Document.RootLayer) == _activeLayer)
        {
            var other = FindNextLeafLayer(Document.RootLayer, _activeLayer);
            if (other == null) return;
        }

        Undo.Record(Document);
        var deleted = _activeLayer;
        _activeLayer = FindNextLeafLayer(Document.RootLayer, deleted) ?? FindFirstLeafLayer(Document.RootLayer);
        deleted.RemoveFromParent();
        InvalidateComposite();
    }

    private static SpriteLayer? FindNextLeafLayer(SpriteNode root, SpriteLayer skip)
    {
        SpriteLayer? result = null;
        FindNextLeafLayerRecursive(root, skip, ref result);
        return result;
    }

    private static void FindNextLeafLayerRecursive(SpriteNode parent, SpriteLayer skip, ref SpriteLayer? result)
    {
        foreach (var child in parent.Children)
        {
            if (child is not SpriteLayer layer)
                continue;
            if (layer.Pixels != null && layer != skip)
            {
                result = layer;
                return;
            }
            FindNextLeafLayerRecursive(layer, skip, ref result);
            if (result != null) return;
        }
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
        Document.UpdateBounds();

        var w = Document.CanvasSize.X;
        var h = Document.CanvasSize.Y;

        _compositePixels ??= new PixelData<Color32>(w, h);
        _compositePixels.Clear();

        CompositeChildren(Document.RootLayer, w, h);

        // Upload to GPU
        var data = _compositePixels.AsByteSpan();
        if (_canvasTexture == null)
            _canvasTexture = Texture.Create(w, h, data, TextureFormat.RGBA8, TextureFilter.Point, "pixel_canvas");
        else
            _canvasTexture.Update(data);
    }

    private void CompositeChildren(SpriteNode parent, int w, int h)
    {
        foreach (var child in parent.Children)
        {
            if (child is not SpriteLayer layer || !layer.Visible)
                continue;

            if (layer.Pixels == null)
            {
                CompositeChildren(layer, w, h);
                continue;
            }

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var src = layer.Pixels[x, y];
                    if (src.A == 0) continue;

                    ref var dst = ref _compositePixels![x, y];
                    if (dst.A == 0)
                    {
                        dst = src;
                    }
                    else
                    {
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
            Graphics.Draw(CanvasRect, order: Document.SortOrder);
        }
    }

    private void DrawPixelGrid()
    {
        var canvas = CanvasRect;
        var pixelWorldSize = canvas.Width / Document.CanvasSize.X;
        var pixelScreenSize = pixelWorldSize * Workspace.Zoom;
        if (pixelScreenSize < 4f) return;

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetColor(new Color(0.5f, 0.5f, 0.5f, 0.15f));
            Graphics.SetSortGroup(5);

            var w = Document.CanvasSize.X;
            var h = Document.CanvasSize.Y;
            var cellW = canvas.Width / w;
            var cellH = canvas.Height / h;

            // Vertical lines
            for (var x = 0; x <= w; x++)
            {
                var xPos = canvas.X + x * cellW;
                Gizmos.DrawLine(
                    new Vector2(xPos, canvas.Y),
                    new Vector2(xPos, canvas.Bottom),
                    EditorStyle.Workspace.DocumentBoundsLineWidth);
            }

            // Horizontal lines
            for (var y = 0; y <= h; y++)
            {
                var yPos = canvas.Y + y * cellH;
                Gizmos.DrawLine(
                    new Vector2(canvas.X, yPos),
                    new Vector2(canvas.Right, yPos),
                    EditorStyle.Workspace.DocumentBoundsLineWidth);
            }
        }
    }

    public Vector2Int WorldToPixel(Vector2 worldPos)
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var local = Vector2.Transform(worldPos, invTransform);
        var canvas = CanvasRect;
        var nx = (local.X - canvas.X) / canvas.Width;
        var ny = (local.Y - canvas.Y) / canvas.Height;
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

    private void Delete()
    {
        if (HasSelection)
            DeleteSelected();
        else
            DeleteActiveLayer();
    }

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
        var canvas = CanvasRect;
        var cellW = canvas.Width / Document.CanvasSize.X;
        var cellH = canvas.Height / Document.CanvasSize.Y;

        var selRect = new Rect(
            canvas.X + sb.X * cellW,
            canvas.Y + sb.Y * cellH,
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

    public void ResizeCanvas(Vector2Int newSize)
    {
        if (newSize == Document.CanvasSize) return;

        var offset = (newSize - Document.CanvasSize) / 2;

        Document.RootLayer.ForEach((SpriteLayer layer) =>
        {
            if (layer.Pixels != null)
            {
                var resized = PixelDataResize.Resized(layer.Pixels, newSize, offset);
                layer.Pixels.Dispose();
                layer.Pixels = resized;
            }
        });

        if (Document.SelectionMask != null)
        {
            var resized = PixelDataResize.Resized(Document.SelectionMask, newSize, offset);
            Document.SelectionMask.Dispose();
            Document.SelectionMask = resized;
        }

        Document.CanvasSize = newSize;

        _canvasTexture?.Dispose();
        _canvasTexture = null;
        _compositePixels?.Dispose();
        _compositePixels = null;

        InvalidateComposite();
        Document.UpdateBounds();
    }

    public override void OnUndoRedo()
    {
        _canvasTexture?.Dispose();
        _canvasTexture = null;
        _compositePixels?.Dispose();
        _compositePixels = null;
        InvalidateComposite();
        if (_activeLayer == null || _activeLayer.Pixels == null)
            _activeLayer = FindFirstLayer();
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

    private struct BrushEdge
    {
        public Vector2 Start;
        public Vector2 End;
    }

    private const int MaxBrushSize = 16;
    private static readonly BrushEdge[]?[] _brushEdgeCache = new BrushEdge[MaxBrushSize + 1][];

    private static BrushEdge[] GetBrushEdges(int brushSize)
    {
        var edges = _brushEdgeCache[brushSize];
        if (edges != null) return edges;
        edges = BuildBrushEdges(brushSize);
        _brushEdgeCache[brushSize] = edges;
        return edges;
    }

    private static BrushEdge[] BuildBrushEdges(int brushSize)
    {
        var list = new List<BrushEdge>();
        for (var dy = 0; dy < brushSize; dy++)
            for (var dx = 0; dx < brushSize; dx++)
            {
                if (!IsInBrush(dx, dy, brushSize)) continue;
                float x0 = dx, y0 = dy, x1 = dx + 1, y1 = dy + 1;
                if (!IsInBrushSafe(dx, dy - 1, brushSize))
                    list.Add(new BrushEdge { Start = new Vector2(x0, y0), End = new Vector2(x1, y0) });
                if (!IsInBrushSafe(dx, dy + 1, brushSize))
                    list.Add(new BrushEdge { Start = new Vector2(x0, y1), End = new Vector2(x1, y1) });
                if (!IsInBrushSafe(dx - 1, dy, brushSize))
                    list.Add(new BrushEdge { Start = new Vector2(x0, y0), End = new Vector2(x0, y1) });
                if (!IsInBrushSafe(dx + 1, dy, brushSize))
                    list.Add(new BrushEdge { Start = new Vector2(x1, y0), End = new Vector2(x1, y1) });
            }
        return list.ToArray();

        static bool IsInBrushSafe(int dx, int dy, int size) =>
            dx >= 0 && dx < size && dy >= 0 && dy < size && IsInBrush(dx, dy, size);
    }

    public void DrawBrushOutline(Vector2Int pixel, Color color)
    {
        var edges = GetBrushEdges(BrushSize);
        if (edges.Length == 0) return;

        var canvas = CanvasRect;
        var cellW = canvas.Width / Document.CanvasSize.X;
        var cellH = canvas.Height / Document.CanvasSize.Y;
        var brushOffset = (BrushSize - 1) / 2;
        var originX = canvas.X + (pixel.X - brushOffset) * cellW;
        var originY = canvas.Y + (pixel.Y - brushOffset) * cellH;
        var halfWidth = EditorStyle.Workspace.DocumentBoundsLineWidth * Gizmos.ZoomRefScale;

        var vertCount = edges.Length * 4;
        var idxCount = edges.Length * 6;
        Span<MeshVertex> verts = stackalloc MeshVertex[vertCount];
        Span<ushort> indices = stackalloc ushort[idxCount];

        for (var i = 0; i < edges.Length; i++)
        {
            ref var edge = ref edges[i];
            var v0 = new Vector2(originX + edge.Start.X * cellW, originY + edge.Start.Y * cellH);
            var v1 = new Vector2(originX + edge.End.X * cellW, originY + edge.End.Y * cellH);

            var delta = v1 - v0;
            var length = delta.Length();
            var dir = delta / length;
            var perp = new Vector2(-dir.Y, dir.X);
            var start = v0 - dir * halfWidth;
            var end = v1 + dir * halfWidth;

            var vi = i * 4;
            verts[vi + 0] = new MeshVertex(start.X - perp.X * halfWidth, start.Y - perp.Y * halfWidth, 0, 0, Color.White);
            verts[vi + 1] = new MeshVertex(start.X + perp.X * halfWidth, start.Y + perp.Y * halfWidth, 1, 0, Color.White);
            verts[vi + 2] = new MeshVertex(end.X + perp.X * halfWidth, end.Y + perp.Y * halfWidth, 1, 1, Color.White);
            verts[vi + 3] = new MeshVertex(end.X - perp.X * halfWidth, end.Y - perp.Y * halfWidth, 0, 1, Color.White);

            var ii = i * 6;
            indices[ii + 0] = (ushort)(vi + 0);
            indices[ii + 1] = (ushort)(vi + 1);
            indices[ii + 2] = (ushort)(vi + 2);
            indices[ii + 3] = (ushort)(vi + 2);
            indices[ii + 4] = (ushort)(vi + 3);
            indices[ii + 5] = (ushort)(vi + 0);
        }

        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(6);
            Graphics.SetColor(color);
            Graphics.Draw(verts, indices);
        }
    }

    public override void Dispose()
    {
        Document.PixelBrushSize = BrushSize;
        Document.PixelBrushColor = BrushColor;
        Grid.PixelsPerUnitOverride = null;

        if (Document.Version != _versionOnOpen && Document.Atlas != null)
            AtlasManager.UpdateSource(Document);

        _canvasTexture?.Dispose();
        _compositePixels?.Dispose();
        base.Dispose();
    }
}
