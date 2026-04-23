//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class PixelSpriteEditor : SpriteEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId LayerToggle { get; }
        public static partial WidgetId ExitEditMode { get; }
        public static partial WidgetId IsolationToggle { get; }
        public static partial WidgetId InspectorToggle { get; }
        public static partial WidgetId BrushButton { get; }
        public static partial WidgetId EraserButton { get; }
        public static partial WidgetId ColorButton { get; }
        public static partial WidgetId RectSelectButton { get; }
        public static partial WidgetId LassoSelectButton { get; }
        public static partial WidgetId MoveButton { get; }
        public static partial WidgetId FillButton { get; }
        public static partial WidgetId DopeSheet { get; }
        public static partial WidgetId AnimatedButton { get; }
        public static partial WidgetId AlphaLockButton { get; }
        public static partial WidgetId TilingButton { get; }
        public static partial WidgetId ShowSkeletonOverlay { get; }

        public static partial WidgetId BrushPopupBrush { get; }
        public static partial WidgetId BrushPopupPencil { get; }
        public static partial WidgetId BrushSizeSlider { get; }
        public static partial WidgetId BrushAlphaSlider { get; }
    }    

    private bool _showLayers = true;
    private bool _showInspector = true;
    private SpriteNode? _selectedNode;
    private Texture? _canvasTexture;
    private PixelData<Color32>? _compositePixels;
    private int _lastCompositeVersion = -1;
    private readonly int _versionOnOpen;
    private int _currentTimeSlot;
    private bool _isPlaying;
    private float _playTimer;
    private bool _showBrushPopup;
    private PixelLayer? _activeLayer;

    public Color32 BrushColor => Document.BrushColor;
    public PixelBrushType BrushType => Document.BrushType;
    public int BrushSize => Document.BrushSize;
    public float BrushHardness => Document.BrushHardness;
    public bool AlphaLock { get; set; }
    public override bool ShowOutliner => _showLayers;
    public override bool ShowInspector => _showInspector;

    public PixelLayer? ActiveLayer
    {
        get => _activeLayer;
        set
        {
            if (value is not { Pixels: not null }) return;
            if (value == _activeLayer) return;
            _activeLayer = value;
            Document.ActiveLayerName = value.Name;
            if (Mode is IActiveLayerHandler handler)
                handler.OnActiveLayerChanged(this);
        }
    }

    public SpriteNode? SelectedNode
    {
        get => _selectedNode;
        set => _selectedNode = value;
    }

    private int CurrentFrameIndex => Document.GetFrameAtTimeSlot(_currentTimeSlot);

    private float CanvasPPU => Document.PixelsPerUnit;

    private int GetMaxWorkingSize()
    {
        if (Document.ConstrainedSize.HasValue)
            return Math.Max(Document.ConstrainedSize.Value.X, Document.ConstrainedSize.Value.Y);
        return EditorApplication.Config.AtlasMaxSpriteSize;
    }

    public RectInt EditablePixelRect
    {
        get
        {
            if (Document.ConstrainedSize.HasValue)
            {
                var cs = Document.ConstrainedSize.Value;
                var ox = (Document.CanvasSize.X - cs.X) / 2;
                var oy = (Document.CanvasSize.Y - cs.Y) / 2;
                return new RectInt(ox, oy, cs.X, cs.Y);
            }
            return new RectInt(0, 0, Document.CanvasSize.X, Document.CanvasSize.Y);
        }
    }

    public Rect CanvasRect
    {
        get
        {
            var epr = EditablePixelRect;
            var w = epr.Width / CanvasPPU;
            var h = epr.Height / CanvasPPU;
            return new Rect(-w / 2, -h / 2, w, h);
        }
    }

    public new PixelSpriteDocument Document => (PixelSpriteDocument)base.Document;

    public PixelSpriteEditor(PixelSpriteDocument document) : base(document)
    {
        _versionOnOpen = document.Version;

        Commands =
        [
            new Command("Exit Edit Mode", Workspace.EndEdit, [InputCode.KeyTab]),
            new Command("Pencil", () => SetMode(PixelBrushType.Pencil), [new KeyBinding(InputCode.KeyY)]),
            new Command("Brush", () => SetMode(PixelBrushType.Brush), [new KeyBinding(InputCode.KeyB)]),
            new Command("Eraser", () => SetMode(new PixelEraserMode()), [new KeyBinding(InputCode.KeyE)]),
            new Command("Rect Select", () => SetMode(new PixelRectSelectMode()), [new KeyBinding(InputCode.KeyM)]),
            new Command("Lasso Select", () => SetMode(new PixelLassoSelectMode()), [new KeyBinding(InputCode.KeyL)]),
            new Command("Move", () => SetMode(new PixelTransformMode()), [new KeyBinding(InputCode.KeyV)]),
            new Command("Eye Dropper", () => SetMode(new PixelEyeDropperMode()), [new KeyBinding(InputCode.KeyI)]),
            new Command("Fill", () => SetMode(new PixelFillMode()), [new KeyBinding(InputCode.KeyG)]),
            new Command("Select All", SelectAll, [new KeyBinding(InputCode.KeyA, ctrl: true)]),
            new Command("Deselect", ClearSelection, [new KeyBinding(InputCode.KeyD, ctrl: true, shift: true)]),
            new Command("Invert Selection", InvertSelection, [new KeyBinding(InputCode.KeyI, ctrl: true, shift: true)]),
            new Command("Toggle Brush/Eraser", ToggleBrushEraser, [new KeyBinding(InputCode.KeyX)]),
            new Command("Copy", CopySelected, [new KeyBinding(InputCode.KeyC, ctrl: true)]),
            new Command("Paste", PasteSelected, [new KeyBinding(InputCode.KeyV, ctrl: true)]),
            new Command("Cut", CutSelected, [new KeyBinding(InputCode.KeyX, ctrl: true)]),
            new Command("Duplicate", DuplicateSelected, [new KeyBinding(InputCode.KeyD, ctrl: true)]),
            new Command("Delete", Delete, [InputCode.KeyDelete]),
            new Command("Increase Brush", IncreaseBrushSize),
            new Command("Decrease Brush", DecreaseBrushSize, [InputCode.KeyLeftBracket]),
            new Command("Rename", BeginRename, [InputCode.KeyF2]),
            new Command("Toggle Playback",     TogglePlayback,     [InputCode.KeySpace]),
            new Command("Previous Frame",      PreviousFrame,      [InputCode.KeyQ]),
            new Command("Next Frame",          NextFrame,          [InputCode.KeyComma]),
            new Command("Add Hold",            AddHoldFrame,       [new KeyBinding(InputCode.KeyH)]),
            new Command("Remove Hold",         RemoveHoldFrame,    [new KeyBinding(InputCode.KeyH, ctrl: true)]),
            new Command("Export to PNG",      ExportToPng,        [new KeyBinding(InputCode.KeyE, ctrl: true, shift: true)]),
        ];

        // Ensure at least one layer exists
        if (Document.Root.Children.Count == 0)
            AddLayer();

        // Expand canvas to max working size so the painter never hits a wall
        var maxWork = GetMaxWorkingSize();
        if (Document.CanvasSize.X < maxWork || Document.CanvasSize.Y < maxWork)
            ResizeCanvas(new Vector2Int(maxWork, maxWork));

        // When animated, always use the current frame's topmost leaf as the active layer
        if (Document.IsAnimated && Document.FrameCount > 0)
        {
            var frame = Document.Root.Children[CurrentFrameIndex];
            ActiveLayer = FindFirstLeafLayer(frame) ?? FindFirstLayer();
        }
        else
        {
            ActiveLayer = RestoreActiveLayer();
        }

        _selectedNode = _activeLayer;
        AlphaLock = Document.AlphaLock;
        Grid.PixelsPerUnitOverride = CanvasPPU;
        SetMode(Document.BrushType);
    }

    private PixelLayer? FindFirstLayer()
    {
        return FindFirstLeafLayer(Document.Root);
    }

    private static PixelLayer? FindFirstLeafLayer(SpriteNode parent)
    {
        if (parent is PixelLayer parentLayer)
            return parentLayer;

        foreach (var child in parent.Children)
        {
            if (child is PixelLayer pixel)
                return pixel;

            if (child.IsExpandable)
            {
                var found = FindFirstLeafLayer(child);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    private PixelLayer? RestoreActiveLayer()
    {
        if (!string.IsNullOrEmpty(Document.ActiveLayerName))
        {
            var node = Document.Root.Find<SpriteNode>(Document.ActiveLayerName);
            if (node is PixelLayer layer && layer.Pixels != null)
                return layer;
        }
        return FindFirstLayer();
    }

    public void AddLayer()
    {
        Undo.Record(Document);
        var layer = new PixelLayer
        {
            Name = $"Layer {Document.Root.Children.Count + 1}",
            Pixels = new PixelData<Color32>(Document.CanvasSize.X, Document.CanvasSize.Y)
        };

        if (Document.IsAnimated)
        {
            // Adding a layer while animated = adding a new frame at the end
            Document.Root.Add(layer);
            Document.IncrementVersion();
            SetCurrentTimeSlot(TimeSlotForFrame(Document.FrameCount - 1));
        }
        else
        {
            Document.Root.Add(layer);
            ActiveLayer = layer;
        }
        SelectedNode = layer;
    }

    public void AddGroup()
    {
        Undo.Record(Document);
        var group = new SpriteGroup
        {
            Name = $"Group {Document.Root.Children.Count + 1}",
        };

        if (Document.IsAnimated)
        {
            // Adding a group while animated = adding a new frame at the end
            Document.Root.Add(group);
            Document.IncrementVersion();
            SetCurrentTimeSlot(TimeSlotForFrame(Document.FrameCount - 1));
        }
        else
        {
            Document.Root.Add(group);
        }
    }

    public void DeleteActiveLayer()
    {
        if (_activeLayer == null) return;

        // Don't delete the last leaf layer
        if (FindFirstLeafLayer(Document.Root) == _activeLayer)
        {
            var other = FindNextLeafLayer(Document.Root, _activeLayer);
            if (other == null) return;
        }

        Undo.Record(Document);
        var deleted = _activeLayer;
        var next = FindNextLeafLayer(Document.Root, deleted) ?? FindFirstLeafLayer(Document.Root);
        _activeLayer = next;
        _selectedNode = next;
        if (next != null)
            Document.ActiveLayerName = next.Name;
        deleted.RemoveFromParent();
        InvalidateComposite();
    }

    private static PixelLayer? FindNextLeafLayer(SpriteNode root, PixelLayer skip)
    {
        PixelLayer? result = null;
        FindNextLeafLayerRecursive(root, skip, ref result);
        return result;
    }

    private static void FindNextLeafLayerRecursive(SpriteNode parent, PixelLayer skip, ref PixelLayer? result)
    {
        foreach (var child in parent.Children)
        {
            if (child is PixelLayer pixel && pixel != skip)
            {
                result = pixel;
                return;
            }
            if (child.IsExpandable)
            {
                FindNextLeafLayerRecursive(child, skip, ref result);
                if (result != null) return;
            }
        }
    }

    public override void Update()
    {
        UpdateAnimation();
        CompositeCanvas();

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(4);
            Document.DrawOrigin();
        }

        DrawCanvas();
        if (Document.ShowTiling) DrawTiling();
        DrawPixelGrid();
        if (Mode is not PixelTransformMode)
            DrawSelectionOutline();
        if (Document.ShowSkeletonOverlay)
            DrawSkeletonOverlay();
        DrawEdges();
        Document.DrawBounds();
        Mode?.Draw();
    }

    public override void LateUpdate()
    {
        Mode?.Update();
    }

    protected override void ExitEdgeEditMode()
    {
        SetMode(new PencilMode());
    }

    public override void UpdateUI()
    {        
        using var column = UI.BeginColumn(new ContainerStyle { 
            Width = EditorStyle.Control.Height,
            Height = Size.Percent(1),
            Margin = new EdgeInsets(16, 8, 16, 0)
        });

        UI.Flex();
    
        using (UI.BeginCursor(new SpriteCursor(EditorAssets.Sprites.CursorArrow)))
        using (UI.BeginColumn(EditorStyle.SpriteEditor.BrushSlider))
        {            
            UI.Image(EditorAssets.Sprites.IconBrush, EditorStyle.Icon.SecondaryLarge);
            using (UI.BeginFlex())
                Document.BrushSize = (int)UI.VerticalSlider(WidgetIds.BrushSizeSlider, BrushSize, style: EditorStyle.Slider.Style with { Step = 1 }, min: 1, max: MaxBrushSize);
        }

        UI.Spacer(16);

        using (UI.BeginCursor(new SpriteCursor(EditorAssets.Sprites.CursorArrow)))
        using (UI.BeginColumn(EditorStyle.SpriteEditor.BrushSlider))
        {
            UI.Image(EditorAssets.Sprites.IconOpacity, EditorStyle.Icon.SecondaryLarge);
            using (UI.BeginFlex())
                Document.BrushColor = Document.BrushColor.WithAlpha((byte)UI.VerticalSlider(WidgetIds.BrushAlphaSlider, BrushColor.A, style: EditorStyle.Slider.Style with { Step = 1f }, min: 0, max: 255));
        }

        UI.Flex();
    }

    public override void UpdateOverlayUI()
    {
        using (FloatingToolbar.Begin())
        {
            if (FloatingToolbar.Button(WidgetIds.FillButton, EditorAssets.Sprites.IconFloodFill, isSelected: Mode is PixelFillMode))
                SetMode(new PixelFillMode());
            EditorUI.Tooltip(WidgetIds.FillButton, "Fill");

            FloatingToolbar.Divider();

            if (FloatingToolbar.Button(WidgetIds.RectSelectButton, EditorAssets.Sprites.IconSelect, isSelected: Mode is PixelRectSelectMode))
                SetMode(new PixelRectSelectMode());
            EditorUI.Tooltip(WidgetIds.RectSelectButton, "Rectangle Select");

            if (FloatingToolbar.Button(WidgetIds.LassoSelectButton, EditorAssets.Sprites.IconSelect, isSelected: Mode is PixelLassoSelectMode))
                SetMode(new PixelLassoSelectMode());
            EditorUI.Tooltip(WidgetIds.LassoSelectButton, "Lasso Select");

            if (FloatingToolbar.Button(WidgetIds.MoveButton, EditorAssets.Sprites.IconMove, isSelected: Mode is PixelTransformMode))
                SetMode(new PixelTransformMode());
            EditorUI.Tooltip(WidgetIds.MoveButton, "Move");

            FloatingToolbar.Divider();

            if (FloatingToolbar.Button(WidgetIds.AlphaLockButton, EditorAssets.Sprites.IconLock, isSelected: AlphaLock))
                AlphaLock = !AlphaLock;
            EditorUI.Tooltip(WidgetIds.AlphaLockButton, "Alpha Lock");

            if (FloatingToolbar.Button(WidgetIds.TilingButton, EditorAssets.Sprites.IconTiling, isSelected: Document.ShowTiling))
                Document.ShowTiling = !Document.ShowTiling;
            EditorUI.Tooltip(WidgetIds.TilingButton, "Tiling Preview");

            if (FloatingToolbar.Button(WidgetIds.ShowSkeletonOverlay, EditorAssets.Sprites.IconBone, isSelected: Document.ShowSkeletonOverlay))
                Document.ShowSkeletonOverlay = !Document.ShowSkeletonOverlay;
            EditorUI.Tooltip(WidgetIds.ShowSkeletonOverlay, "Skeleton Overlay");

            FloatingToolbar.Divider();

            if (Document.IsAnimated && Document.FrameCount > 1)
            {
                FloatingToolbar.Row();
                DopeSheetUI();
            }
        }
    }

    private RectInt _compositeRect;

    private void CompositeCanvas()
    {
        if (_lastCompositeVersion == Document.Version)
            return;

        _lastCompositeVersion = Document.Version;
        Document.UpdateBounds();

        var epr = EditablePixelRect;

        // Recreate buffers if editable region changed size
        if (_compositePixels == null || _compositeRect != epr)
        {
            _compositePixels?.Dispose();
            _compositePixels = new PixelData<Color32>(epr.Width, epr.Height);
            _canvasTexture?.Dispose();
            _canvasTexture = null;
            _compositeRect = epr;
        }

        _compositePixels.Clear();
        CompositeChildren(Document.Root, epr);

        var data = _compositePixels.AsByteSpan();
        if (_canvasTexture == null)
            _canvasTexture = Texture.Create(epr.Width, epr.Height, data, TextureFormat.RGBA8, TextureFilter.Point, "pixel_canvas");
        else
            _canvasTexture.Update(data);
    }

    private void CompositeChildren(SpriteNode parent, in RectInt epr)
    {
        foreach (var child in parent.Children)
        {
            if (!child.Visible) continue;

            if (child.IsExpandable)
            {
                CompositeChildren(child, epr);
                continue;
            }

            if (child is not PixelLayer layer || layer.Pixels == null)
                continue;

            for (var y = 0; y < epr.Height; y++)
            {
                for (var x = 0; x < epr.Width; x++)
                {
                    var src = layer.Pixels[epr.X + x, epr.Y + y];
                    if (src.A == 0)
                    {
                        // Apron pixels (A=0 with RGB set) exist so bilinear filtering has a
                        // color to interpolate toward instead of black. Preserve them into the
                        // composite only where no opaque coverage exists yet.
                        if ((src.R | src.G | src.B) == 0) continue;
                        ref var apronDst = ref _compositePixels![x, y];
                        if (apronDst.A == 0 && (apronDst.R | apronDst.G | apronDst.B) == 0)
                            apronDst = src;
                        continue;
                    }

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
            Graphics.SetTextureFilter(Document.TextureFilterOverride ?? TextureFilter.Point);
            Graphics.SetColor(Color.White);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.Draw(CanvasRect, order: Document.SortOrder);
        }
    }

    private void DrawTiling()
    {
        if (_canvasTexture == null) return;

        var canvas = CanvasRect;
        using (Graphics.PushState())
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetTexture(_canvasTexture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTextureFilter(Document.TextureFilterOverride ?? TextureFilter.Point);
            Graphics.SetColor(Color.White);
            Graphics.SetLayer(EditorLayer.DocumentEditor);

            for (var dy = -2; dy <= 2; dy++)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var tileRect = new Rect(
                        canvas.X + dx * canvas.Width,
                        canvas.Y + dy * canvas.Height,
                        canvas.Width,
                        canvas.Height);
                    Graphics.Draw(tileRect, order: Document.SortOrder);
                }
            }
        }
    }

    private void DrawPixelGrid()
    {
        var canvas = CanvasRect;
        var pixelWorldSize = canvas.Width / Document.CanvasSize.X;
        var pixelScreenSize = pixelWorldSize * Workspace.Zoom;
        if (pixelScreenSize < 4f) return;

        using (Gizmos.PushState(EditorLayer.PixelGrid))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetColor(new Color(0.5f, 0.5f, 0.5f, 0.15f));

            var epr = EditablePixelRect;
            var w = epr.Width;
            var h = epr.Height;
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

    public Vector2Int WorldToPixelSnapped(Vector2 worldPos)
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var local = Vector2.Transform(worldPos, invTransform);
        var canvas = CanvasRect;
        var epr = EditablePixelRect;
        var nx = (local.X - canvas.X) / canvas.Width;
        var ny = (local.Y - canvas.Y) / canvas.Height;
        return new Vector2Int(
            epr.X + (int)MathF.Floor(nx * epr.Width),
            epr.Y + (int)MathF.Floor(ny * epr.Height));
    }

    public Vector2 WorldToPixel(Vector2 worldPos)
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var local = Vector2.Transform(worldPos, invTransform);
        var canvas = CanvasRect;
        var epr = EditablePixelRect;
        var nx = (local.X - canvas.X) / canvas.Width;
        var ny = (local.Y - canvas.Y) / canvas.Height;
        return new Vector2(
            epr.X + nx * epr.Width,
            epr.Y + ny * epr.Height);
    }

    public bool IsPixelInBounds(Vector2Int pixel) =>
        pixel.X >= 0 && pixel.X < Document.CanvasSize.X &&
        pixel.Y >= 0 && pixel.Y < Document.CanvasSize.Y;

    public bool IsPixelInConstraint(Vector2Int pixel)
    {
        var r = EditablePixelRect;
        return pixel.X >= r.X && pixel.X < r.X + r.Width &&
               pixel.Y >= r.Y && pixel.Y < r.Y + r.Height;
    }

    public Vector2Int WrapPixelForTiling(Vector2Int pixel)
    {
        if (!Document.ShowTiling) return pixel;
        var r = EditablePixelRect;
        return new Vector2Int(
            r.X + ((pixel.X - r.X) % r.Width + r.Width) % r.Width,
            r.Y + ((pixel.Y - r.Y) % r.Height + r.Height) % r.Height);
    }

    public void InvalidateComposite()
    {
        _lastCompositeVersion = -1;
    }

    public void InvalidateActiveLayerPreview()
    {
        _activeLayer?.InvalidatePreview();
    }

    private void ToggleBrushEraser()
    {
        if (Mode is PixelEraserMode)
            SetMode(new PencilMode());
        else
            SetMode(new PixelEraserMode());
    }

    private void Delete()
    {
        if (HasSelection)
            DeleteSelected();
        else
            DeleteActiveLayer();
    }

    public void ResizeCanvas(Vector2Int newSize)
    {
        if (newSize == Document.CanvasSize) return;

        var offset = (newSize - Document.CanvasSize) / 2;

        Document.Root.ForEach((PixelLayer layer) =>
        {
            if (layer.Pixels != null)
            {
                var resized = PixelDataResize.Resized(layer.Pixels, newSize, offset);
                layer.Pixels.Dispose();
                layer.Pixels = resized;
            }
            else
            {
                layer.Pixels = new PixelData<Color32>(newSize.X, newSize.Y);
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
        _activeLayer = RestoreActiveLayer();
        _selectedNode = _activeLayer;
        base.OnUndoRedo();
        _canvasTexture?.Dispose();
        _canvasTexture = null;
        _compositePixels?.Dispose();
        _compositePixels = null;
        InvalidateComposite();
        ResetPreviews();
    }

    private void SetCurrentTimeSlot(int timeSlot)
    {
        var maxSlots = Document.TotalTimeSlots;
        var newSlot = Math.Clamp(timeSlot, 0, maxSlots - 1);
        if (newSlot != _currentTimeSlot)
        {
            _currentTimeSlot = newSlot;

            // Set topmost leaf layer in the current frame as active
            if (Document.IsAnimated && Document.FrameCount > 0)
            {
                var frame = Document.Root.Children[CurrentFrameIndex];
                var firstLayer = FindFirstLeafLayer(frame);
                if (firstLayer != null)
                    ActiveLayer = firstLayer;
            }
        }
    }

    private int TimeSlotForFrame(int frameIndex)
    {
        var slot = 0;
        for (var i = 0; i < frameIndex && i < Document.Root.Children.Count; i++)
            slot += 1 + Document.Root.Children[i].Hold;
        return slot;
    }

    private bool IsTimeSlotInRange(int frameIndex, int timeSlot)
    {
        var accumulated = 0;
        for (var fi = 0; fi < Document.Root.Children.Count; fi++)
        {
            var slots = 1 + Document.Root.Children[fi].Hold;
            if (fi == frameIndex)
                return timeSlot >= accumulated && timeSlot < accumulated + slots;
            accumulated += slots;
        }
        return false;
    }

    private void TogglePlayback()
    {
        _isPlaying = !_isPlaying;
        _playTimer = 0;
    }

    private void NextFrame()
    {
        var frameCount = Document.FrameCount;
        if (frameCount <= 1) return;

        var fi = CurrentFrameIndex;
        fi = (fi + 1) % frameCount;
        SetCurrentTimeSlot(TimeSlotForFrame(fi));
    }

    private void PreviousFrame()
    {
        var frameCount = Document.FrameCount;
        if (frameCount <= 1) return;

        var fi = CurrentFrameIndex;
        fi = fi == 0 ? frameCount - 1 : fi - 1;
        SetCurrentTimeSlot(TimeSlotForFrame(fi));
    }

    private void ToggleAnimation()
    {
        Undo.Record(Document);
        if (Document.IsAnimated)
        {
            Document.DisableAnimation();
        }
        else
        {
            Document.EnableAnimation();
            SetCurrentTimeSlot(0);
        }
        InvalidateComposite();
    }

    private void AddHoldFrame()
    {
        if (!Document.IsAnimated || Document.FrameCount == 0) return;
        Undo.Record(Document);
        Document.Root.Children[CurrentFrameIndex].Hold++;
    }

    private void RemoveHoldFrame()
    {
        if (!Document.IsAnimated || Document.FrameCount == 0) return;
        var frame = Document.Root.Children[CurrentFrameIndex];
        if (frame.Hold <= 0) return;
        Undo.Record(Document);
        frame.Hold--;
    }

    private void UpdateAnimation()
    {
        if (!_isPlaying || Document.TotalTimeSlots <= 1)
            return;

        _playTimer += Time.DeltaTime;
        var slotDuration = 1f / SpriteDocument.DefaultFrameRate;

        if (_playTimer >= slotDuration)
        {
            _playTimer = 0;
            var nextSlot = (_currentTimeSlot + 1) % Document.TotalTimeSlots;
            SetCurrentTimeSlot(nextSlot);
        }
    }

    private void DopeSheetUI()
    {
        var maxSlots = Sprite.MaxFrames;
        var usedSlots = Document.TotalTimeSlots;
        var blockCount = Math.Max((usedSlots + 3) / 4, 5);

        using (UI.BeginColumn(EditorStyle.Dopesheet.FloatingDopesheet))
        {
            using (UI.BeginRow(EditorStyle.Dopesheet.FloatingHeaderContainer))
            {
                for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
                {
                    if (blockIndex > 0)
                        UI.Container(EditorStyle.Dopesheet.FloatingTimeTick);

                    using (UI.BeginContainer(EditorStyle.Dopesheet.TimeBlock))
                    {
                        UI.Text(AnimationEditor.FrameTimeStrings[blockIndex], EditorStyle.Dopesheet.TimeText);
                    }
                }

                UI.Flex();
            }

            UI.Spacer(1);

            using (UI.BeginRow(EditorStyle.Dopesheet.FloatingLayerRow))
            {
                var slotIndex = 0;
                for (ushort fi = 0; fi < Document.Root.Children.Count && slotIndex < maxSlots; fi++)
                {
                    var animFrame = Document.Root.Children[fi];
                    var isCurrentSlot = IsTimeSlotInRange(fi, _currentTimeSlot);

                    using (UI.BeginRow(WidgetIds.DopeSheet + fi))
                    {
                        if (UI.WasPressed())
                            SetCurrentTimeSlot(TimeSlotForFrame(fi));

                        using (UI.BeginContainer(isCurrentSlot
                            ? EditorStyle.Dopesheet.FloatingSelectedFrame
                            : EditorStyle.Dopesheet.FloatingFrame))
                        {
                            UI.Container(isCurrentSlot
                                ? EditorStyle.Dopesheet.FloatingSelectedFrameDot
                                : EditorStyle.Dopesheet.FloatingFrameDot);
                        }

                        slotIndex++;

                        var hold = animFrame.Hold;
                        for (int h = 0; h < hold && slotIndex < maxSlots; h++, slotIndex++)
                        {
                            if (h < hold - 1)
                                UI.Container(isCurrentSlot
                                    ? EditorStyle.Dopesheet.FloatingSelectedHoldSeparator
                                    : EditorStyle.Dopesheet.FloatingHoldSeparator);

                            using (UI.BeginContainer(isCurrentSlot
                                ? EditorStyle.Dopesheet.FloatingSelectedFrame
                                : EditorStyle.Dopesheet.FloatingFrame))
                            {
                            }
                        }
                    }
                }

                UI.Flex();
            }
        }
    }

    private void ExportToPng()
    {
        var pngBytes = Document.RasterizeColorToPng();
        if (pngBytes.Length == 0)
        {
            Log.Warning("Nothing to export: sprite has no content.");
            return;
        }

        var defaultName = Document.Name + ".png";
        var path = NativeFileDialog.ShowSaveFileDialog(
            Application.Platform.WindowHandle,
            "PNG Files\0*.png\0All Files\0*.*\0",
            "png",
            defaultName);

        if (path == null) return;

        File.WriteAllBytes(path, pngBytes);
        Log.Info($"Exported PNG to: {path}");
    }

    public override void Dispose()
    {
        Document.BrushSize = BrushSize;
        Document.BrushColor = BrushColor;
        Document.AlphaLock = AlphaLock;
        Grid.PixelsPerUnitOverride = null;

        if (Document.Version != _versionOnOpen && Document.Atlas != null)
            AtlasManager.UpdateSource(Document);

        _canvasTexture?.Dispose();
        _compositePixels?.Dispose();
        DisposePreviews();
        base.Dispose();
    }

    private void BrushPopup()
    {
        if (!_showBrushPopup) return;
        using var popup = UI.BeginPopup(WidgetIds.BrushButton, EditorStyle.PopupBelow with { AnchorRect = UI.GetElementWorldRect(WidgetIds.BrushButton) });
        using var column = UI.BeginColumn(EditorStyle.Popup.Root with { Spacing = 4});
 
        if (UI.Button(WidgetIds.BrushPopupBrush, EditorAssets.Sprites.IconBrush, EditorStyle.Button.ToggleIcon, isSelected: BrushType == PixelBrushType.Brush))
        {
            SetMode(PixelBrushType.Brush);
            _showBrushPopup = false;
        }

        if (UI.Button(WidgetIds.BrushPopupPencil, EditorAssets.Sprites.IconEdit, EditorStyle.Button.ToggleIcon, isSelected: BrushType == PixelBrushType.Pencil))
        {
            SetMode(PixelBrushType.Pencil);
            _showBrushPopup = false;
        }            
    }

    public override void ToolbarUI()
    {
        base.ToolbarUI();

        if (UI.Button(WidgetIds.LayerToggle, EditorAssets.Sprites.IconLayer, EditorStyle.Button.ToggleIcon, isSelected: _showLayers))  
            _showLayers = !_showLayers;

        EditorUI.PanelSeparator();

        if (UI.Button(WidgetIds.ExitEditMode, EditorAssets.Sprites.IconEdit, EditorStyle.Button.ToggleIcon, isSelected: true))  
            Workspace.EndEdit();

        if (UI.Button(WidgetIds.IsolationToggle, EditorAssets.Sprites.IconIsolate, EditorStyle.Button.ToggleIcon, isSelected: Workspace.Isolation))  
            Workspace.ToggleIsolation();

        UI.Flex();

        var isPaintMode = Mode is BrushMode or PencilMode;
        if (UI.Button(WidgetIds.BrushButton, BrushType == PixelBrushType.Brush ? EditorAssets.Sprites.IconBrush : EditorAssets.Sprites.IconEdit, EditorStyle.Button.ToggleIcon, isSelected: isPaintMode))
        {
            if (isPaintMode)
                _showBrushPopup = true;
            else
                SetMode(PixelBrushType.Brush);
        }

        BrushPopup();            

        if (UI.Button(WidgetIds.EraserButton, EditorAssets.Sprites.IconEraser, EditorStyle.Button.ToggleIcon, isSelected: Mode is PixelEraserMode))
            SetMode(new PixelEraserMode());

        var color = BrushColor.ToColor();
        var newColor = EditorUI.ColorButton(WidgetIds.BrushColor, color, style: new ColorButtonStyle { Popup = EditorStyle.PopupBelow, ShowAlpha = false, ShowClose = false });
        if (newColor != BrushColor)
            Document.BrushColor = newColor;
        EditorUI.Tooltip(WidgetIds.BrushColor, "Brush Color");

        EditorUI.PanelSeparator();

        if (UI.Button(WidgetIds.InspectorToggle, EditorAssets.Sprites.IconInfo, EditorStyle.Button.ToggleIcon, isSelected: _showInspector))  
            _showInspector = !_showInspector;
    }    

    public void SetMode(PixelBrushType brushType)
    {
        Document.BrushType = brushType;
        SetMode(brushType switch
        {
            PixelBrushType.Brush => new BrushMode(),
            PixelBrushType.Pencil => new PencilMode(),
            _ => throw new ArgumentOutOfRangeException(nameof(brushType), brushType, null)
        });
    }

    private void IncreaseBrushSize() => Document.BrushSize = Math.Min(Document.BrushSize + 1, 16);  
    private void DecreaseBrushSize() => Document.BrushSize = Math.Max(Document.BrushSize - 1, 1);
}
