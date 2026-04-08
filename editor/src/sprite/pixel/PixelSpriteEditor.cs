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
        public static partial WidgetId PencilButton { get; }
        public static partial WidgetId EraserButton { get; }
        public static partial WidgetId ColorButton { get; }
        public static partial WidgetId EyeDropperButton { get; }
        public static partial WidgetId RectSelectButton { get; }
        public static partial WidgetId LassoSelectButton { get; }
        public static partial WidgetId MoveButton { get; }
        public static partial WidgetId FillButton { get; }
        public static partial WidgetId DopeSheet { get; }
        public static partial WidgetId AddFrameButton { get; }
        public static partial WidgetId AlphaLockButton { get; }
        public static partial WidgetId TilingButton { get; }
    }

    public Color32 BrushColor { get; set; } = Color32.Black;
    public int BrushSize { get; set; } = 1;
    public bool AlphaLock { get; set; }

    private PixelLayer? _activeLayer;
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

    private SpriteNode? _selectedNode;
    public SpriteNode? SelectedNode
    {
        get => _selectedNode;
        set => _selectedNode = value;
    }

    private Texture? _canvasTexture;
    private PixelData<Color32>? _compositePixels;
    private int _lastCompositeVersion = -1;
    private readonly int _versionOnOpen;

    private int _currentTimeSlot;
    private bool _isPlaying;
    private float _playTimer;

    private int CurrentFrameIndex => Document.GetFrameAtTimeSlot(_currentTimeSlot);

    private float CanvasPPU => Document.PixelsPerUnitOverride ?? 32f;

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

    public override bool ShowInspector => true;
    public override bool ShowOutliner => true;

    public new PixelSpriteDocument Document => (PixelSpriteDocument)base.Document;

    public PixelSpriteEditor(PixelSpriteDocument document) : base(document)
    {
        _versionOnOpen = document.Version;

        Commands =
        [
            new Command("Exit Edit Mode", Workspace.EndEdit, [InputCode.KeyTab]),
            new Command("Pencil", () => SetMode(new PencilMode()), [new KeyBinding(InputCode.KeyB)]),
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
            new Command("Increase Brush", () => BrushSize = Math.Min(BrushSize + 1, 16), [InputCode.KeyRightBracket]),
            new Command("Decrease Brush", () => BrushSize = Math.Max(BrushSize - 1, 1), [InputCode.KeyLeftBracket]),
            new Command("Rename", BeginRename, [InputCode.KeyF2]),
            new Command("Toggle Playback",     TogglePlayback,     [InputCode.KeySpace]),
            new Command("Previous Frame",      PreviousFrame,      [InputCode.KeyQ]),
            new Command("Next Frame",          NextFrame,          [InputCode.KeyComma]),
            new Command("Insert Frame Before", InsertFrameBefore,  [new KeyBinding(InputCode.KeyI, ctrl: true)]),
            new Command("Insert Frame After",  InsertFrameAfter,   [new KeyBinding(InputCode.KeyO, ctrl: true)]),
            new Command("Delete Frame",        DeleteCurrentFrame, [new KeyBinding(InputCode.KeyX, shift: true)]),
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

        ActiveLayer = RestoreActiveLayer();
        _selectedNode = _activeLayer;
        BrushSize = Document.BrushSize;
        BrushColor = Document.BrushColor;
        AlphaLock = Document.AlphaLock;
        Grid.PixelsPerUnitOverride = CanvasPPU;
        ApplyCurrentFrameVisibility();
        SetMode(new PencilMode());
    }

    private PixelLayer? FindFirstLayer()
    {
        return FindFirstLeafLayer(Document.Root);
    }

    private static PixelLayer? FindFirstLeafLayer(SpriteNode parent)
    {
        foreach (var child in parent.Children)
        {
            if (child is PixelLayer pixel)
                return pixel;

            if (child is SpriteGroup group)
            {
                var found = FindFirstLeafLayer(group);
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
        Document.Root.Add(layer);
        SelectedNode = layer;
    }

    public void AddGroup()
    {
        Undo.Record(Document);
        var group = new SpriteGroup
        {
            Name = $"Group {Document.Root.Children.Count + 1}",
        };
        Document.Root.Add(group);
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
            if (child is SpriteGroup group)
            {
                FindNextLeafLayerRecursive(group, skip, ref result);
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
        using (UI.BeginCursor(new SpriteCursor(EditorAssets.Sprites.CursorArrow)))
        using (FloatingToolbar.Begin())
        {
            var color = BrushColor.ToColor();
            var newColor = FloatingToolbar.ColorButton(WidgetIds.BrushColor, color).ToColor32();
            if (newColor != BrushColor)
                BrushColor = newColor;

            FloatingToolbar.Divider();

            if (FloatingToolbar.Button(WidgetIds.PencilButton, EditorAssets.Sprites.IconEdit, isSelected: Mode is PencilMode))
                SetMode(new PencilMode());

            if (FloatingToolbar.Button(WidgetIds.EraserButton, EditorAssets.Sprites.IconEraser, isSelected: Mode is PixelEraserMode))
                SetMode(new PixelEraserMode());

            if (FloatingToolbar.Button(WidgetIds.FillButton, EditorAssets.Sprites.IconFloodFill, isSelected: Mode is PixelFillMode))
                SetMode(new PixelFillMode());

            FloatingToolbar.Divider();

            if (FloatingToolbar.Button(WidgetIds.RectSelectButton, EditorAssets.Sprites.IconSelect, isSelected: Mode is PixelRectSelectMode))
                SetMode(new PixelRectSelectMode());

            if (FloatingToolbar.Button(WidgetIds.LassoSelectButton, EditorAssets.Sprites.IconSelect, isSelected: Mode is PixelLassoSelectMode))
                SetMode(new PixelLassoSelectMode());

            if (FloatingToolbar.Button(WidgetIds.MoveButton, EditorAssets.Sprites.IconMove, isSelected: Mode is PixelTransformMode))
                SetMode(new PixelTransformMode());

            FloatingToolbar.Divider();

            if (FloatingToolbar.Button(WidgetIds.EyeDropperButton, EditorAssets.Sprites.IconPreview, isSelected: Mode is PixelEyeDropperMode))
                SetMode(new PixelEyeDropperMode());

            FloatingToolbar.Divider();

            if (FloatingToolbar.Button(WidgetIds.AlphaLockButton, EditorAssets.Sprites.IconLock, isSelected: AlphaLock))
                AlphaLock = !AlphaLock;

            if (FloatingToolbar.Button(WidgetIds.TilingButton, EditorAssets.Sprites.IconTiling, isSelected: Document.ShowTiling))
                Document.ShowTiling = !Document.ShowTiling;

            FloatingToolbar.Divider();

            if (FloatingToolbar.Button(WidgetIds.AddFrameButton, EditorAssets.Sprites.IconKeyframe))
                InsertFrameAfter();

            if (Document.AnimFrames.Count > 1)
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

            if (child is SpriteGroup group)
            {
                CompositeChildren(group, epr);
                continue;
            }

            if (child is not PixelLayer layer || layer.Pixels == null)
                continue;

            for (var y = 0; y < epr.Height; y++)
            {
                for (var x = 0; x < epr.Width; x++)
                {
                    var src = layer.Pixels[epr.X + x, epr.Y + y];
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
            Graphics.SetTextureFilter(TextureFilter.Point);
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

    public Vector2Int WorldToPixel(Vector2 worldPos)
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

    public bool IsPixelInBounds(Vector2Int pixel) =>
        pixel.X >= 0 && pixel.X < Document.CanvasSize.X &&
        pixel.Y >= 0 && pixel.Y < Document.CanvasSize.Y;

    public bool IsPixelInConstraint(Vector2Int pixel)
    {
        var r = EditablePixelRect;
        return pixel.X >= r.X && pixel.X < r.X + r.Width &&
               pixel.Y >= r.Y && pixel.Y < r.Y + r.Height;
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
        if (Mode is PencilMode)
            SetMode(new PixelEraserMode());
        else
            SetMode(new PencilMode());
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
        ApplyCurrentFrameVisibility();
    }

    // --- Animation ---

    private void ApplyCurrentFrameVisibility()
    {
        var fi = CurrentFrameIndex;
        if (fi < Document.AnimFrames.Count)
            Document.AnimFrames[fi].ApplyVisibility(Document.Root);
        InvalidateComposite();
    }

    private void SetCurrentTimeSlot(int timeSlot)
    {
        var maxSlots = Document.TotalTimeSlots;
        var newSlot = Math.Clamp(timeSlot, 0, maxSlots - 1);
        if (newSlot != _currentTimeSlot)
        {
            _currentTimeSlot = newSlot;
            ApplyCurrentFrameVisibility();
        }
    }

    private int TimeSlotForFrame(int frameIndex)
    {
        var slot = 0;
        for (var f = 0; f < frameIndex && f < Document.AnimFrames.Count; f++)
            slot += 1 + Document.AnimFrames[f].Hold;
        return slot;
    }

    private bool IsTimeSlotInRange(int frameIndex, int timeSlot)
    {
        var accumulated = 0;
        for (var f = 0; f < Document.AnimFrames.Count; f++)
        {
            var slots = 1 + Document.AnimFrames[f].Hold;
            if (f == frameIndex)
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
        if (Document.AnimFrames.Count <= 1)
            return;

        var fi = CurrentFrameIndex;
        fi = (fi + 1) % Document.AnimFrames.Count;
        SetCurrentTimeSlot(TimeSlotForFrame(fi));
    }

    private void PreviousFrame()
    {
        if (Document.AnimFrames.Count <= 1)
            return;

        var fi = CurrentFrameIndex;
        fi = fi == 0 ? Document.AnimFrames.Count - 1 : fi - 1;
        SetCurrentTimeSlot(TimeSlotForFrame(fi));
    }

    private void InsertFrameBefore()
    {
        Undo.Record(Document);
        var fi = CurrentFrameIndex;
        var newFrame = Document.InsertFrame(fi);
        if (newFrame >= 0)
            SetCurrentTimeSlot(TimeSlotForFrame(newFrame));
        InvalidateComposite();
    }

    private void InsertFrameAfter()
    {
        Undo.Record(Document);
        var fi = CurrentFrameIndex;
        var newFrame = Document.InsertFrame(fi + 1);
        if (newFrame >= 0)
            SetCurrentTimeSlot(TimeSlotForFrame(newFrame));
        InvalidateComposite();
    }

    private void DeleteCurrentFrame()
    {
        if (Document.AnimFrames.Count <= 1) return;
        Undo.Record(Document);
        var fi = Document.DeleteFrame(CurrentFrameIndex);
        SetCurrentTimeSlot(TimeSlotForFrame(fi));
        InvalidateComposite();
    }

    private void AddHoldFrame()
    {
        var fi = CurrentFrameIndex;
        if (fi >= Document.AnimFrames.Count) return;
        Undo.Record(Document);
        Document.AnimFrames[fi].Hold++;
    }

    private void RemoveHoldFrame()
    {
        var fi = CurrentFrameIndex;
        if (fi >= Document.AnimFrames.Count) return;
        var frame = Document.AnimFrames[fi];
        if (frame.Hold <= 0)
            return;

        Undo.Record(Document);
        frame.Hold = Math.Max(0, frame.Hold - 1);
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
                for (ushort fi = 0; fi < Document.AnimFrames.Count && slotIndex < maxSlots; fi++)
                {
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

                        var hold = fi < Document.AnimFrames.Count ? Document.AnimFrames[fi].Hold : 0;
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
}
