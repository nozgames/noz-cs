//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class VectorSpriteEditor : SpriteEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId TileButton { get; }
        public static partial WidgetId SubtractButton { get; }
        public static partial WidgetId FirstOpacity { get; }
        public static partial WidgetId DopeSheet { get; }
        public static partial WidgetId FillColorButton { get; }
        public static partial WidgetId StrokeColor { get; }
        public static partial WidgetId StrokeWidth { get; }
        public static partial WidgetId StrokeJoinRound { get; }
        public static partial WidgetId StrokeJoinMiter { get; }
        public static partial WidgetId StrokeJoinBevel { get; }
        public static partial WidgetId AddFrameButton { get; }
        public static partial WidgetId PlayButton { get; }
        public static partial WidgetId ConstraintDropDown { get; }
        public static partial WidgetId SkeletonDropDown { get; }
        public static partial WidgetId BoneDropDown { get; }
        public static partial WidgetId ShowInSkeleton { get; }
        public static partial WidgetId ShowSkeletonOverlay { get; }
        public static partial WidgetId PathNormal { get; }
        public static partial WidgetId PathSubtract { get; }
        public static partial WidgetId PathClip { get; }
        public static partial WidgetId BoolUnion { get; }
        public static partial WidgetId BoolSubtract { get; }
        public static partial WidgetId BoolIntersect { get; }
        public static partial WidgetId FillColor { get; }
        public static partial WidgetId AddEdgesButton { get; }
        public static partial WidgetId RemoveEdgesButton { get; }
        public static partial WidgetId EdgeTop { get; }
        public static partial WidgetId EdgeLeft { get; }
        public static partial WidgetId EdgeBottom { get; }
        public static partial WidgetId EdgeRight { get; }
        public static partial WidgetId ExportToggle { get; }
        public static partial WidgetId PenToolButton { get; }
        public static partial WidgetId RectToolButton { get; }
        public static partial WidgetId CircleToolButton { get; }
        public static partial WidgetId DopeSheetToggle { get; }
        public static partial WidgetId VModeButton { get; }
        public static partial WidgetId AModeButton { get; }
        public static partial WidgetId BevelModeButton { get; }
        public static partial WidgetId SortOrder { get; }
        public static partial WidgetId ContextMenu { get; }
        public static partial WidgetId OnionSkinButton { get; }
    }

    private int _currentTimeSlot;
    private bool _isPlaying;
    private bool _onionSkin;
    private float _playTimer;
    private readonly int _versionOnOpen;

    public int CurrentTimeSlot => _currentTimeSlot;

    public bool IsPlaying => _isPlaying;

    public static List<SpritePath> HitPaths => _hitPaths;

    public override bool ShowInspector => true;
    public override bool ShowOutliner => true;

    private int CurrentFrameIndex =>
        Document.GetFrameAtTimeSlot(_currentTimeSlot);

    public VectorSpriteEditor(SpriteDocument doc) : base(doc)
    {
        _versionOnOpen = doc.Version;

        if (doc.IsMutable)
        {
            Commands =
            [
                ..GetShapeCommands(),
                new Command("Exit Edit Mode",       Workspace.EndEdit,          [InputCode.KeyTab]),
                new Command("Origin to Center",     CenterShape,                [new KeyBinding(InputCode.KeyC, shift:true)]),
                new Command("Flip Horizontal",      () => FlipAxis(true)),
                new Command("Flip Vertical",        () => FlipAxis(false)),
                new Command("Bring Forward",        () => MovePathInOrder(-1),  [InputCode.KeyRightBracket]),
                new Command("Send Backward",        () => MovePathInOrder(1),   [InputCode.KeyLeftBracket]),
                new Command("Toggle Playback",      TogglePlayback,             [InputCode.KeySpace]),
                new Command("Previous Frame",       PreviousFrame,              [InputCode.KeyQ]),
                new Command("Next Frame",           NextFrame,                  [InputCode.KeyE]),
                new Command("Insert Frame Before",  InsertFrameBefore,          [new KeyBinding(InputCode.KeyI, ctrl:true)]),
                new Command("Insert Frame After",   InsertFrameAfter,           [new KeyBinding(InputCode.KeyO, ctrl:true)]),
                new Command("Delete Frame",         DeleteCurrentFrame,         [new KeyBinding(InputCode.KeyX, shift:true)]),
                new Command("Add Hold",             AddHoldFrame,               [new KeyBinding(InputCode.KeyH)]),
                new Command("Remove Hold",          RemoveHoldFrame,            [new KeyBinding(InputCode.KeyH, ctrl:true)]),
                new Command("Toggle Onion Skin",    ToggleOnionSkin,            [new KeyBinding(InputCode.KeyO, shift:true)]),
                new Command("Eye Dropper",          () => SetMode(SpriteEditMode.EyeDropper), [new KeyBinding(InputCode.KeyI)]),
                new Command("Boolean Union",        BooleanUnion,               [new KeyBinding(InputCode.KeyU, ctrl:true, shift:true)]),
                new Command("Boolean Subtract",     BooleanSubtract,            [new KeyBinding(InputCode.KeyD, ctrl:true, shift:true)]),
                new Command("Boolean Intersect",    BooleanIntersect,           [new KeyBinding(InputCode.KeyI, ctrl:true, shift:true)]),
                new Command("Export to PNG",        ExportToPng,                [new KeyBinding(InputCode.KeyE, ctrl:true, shift:true)]),
            ];

            ApplyCurrentFrameVisibility();
            SetMode(new TransformMode());
        }
        else
        {
            Commands =
            [
                new Command("Exit Edit Mode",       Workspace.EndEdit,          [InputCode.KeyTab]),
            ];
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
        ClearSelection();
        EditorUI.ClosePopup();

        if (Document.Version != _versionOnOpen && Document.Atlas != null)
            AtlasManager.UpdateSource(Document);

        base.Dispose();
    }

    public override void OnUndoRedo()
    {
        base.OnUndoRedo();
        MarkDirty();
        RebuildSelectedPaths();
    }

    public override void OpenContextMenu(WidgetId popupId)
    {
        var vMode = CurrentMode == SpriteEditMode.Transform;
        var hasPath = HasPathSelection && _selectedPaths.Count > 0;
        var hasSelection = hasPath || HasLayerSelection;
        var multiPath = _selectedPaths.Count >= 2 && vMode;
        var singleNode = (HasLayerSelection && _selectedLayers.Count == 1) ||
                         (!HasLayerSelection && _selectedPaths.Count == 1);
        var canDelete = hasPath || HasLayerSelection || (CurrentMode != SpriteEditMode.Transform && GetPathWithSelection() != null);

        var items = new List<PopupMenuItem>
        {
            PopupMenuItem.Item("Cut", CutSelected, new KeyBinding(InputCode.KeyX, ctrl: true),
                enabled: () => hasPath && vMode),
            PopupMenuItem.Item("Copy", CopySelected, new KeyBinding(InputCode.KeyC, ctrl: true),
                enabled: () => hasPath && vMode),
            PopupMenuItem.Item("Paste", PasteSelected, new KeyBinding(InputCode.KeyV, ctrl: true),
                enabled: () => Clipboard.Get<PathClipboardData>() != null),
            PopupMenuItem.Item("Duplicate", DuplicateSelected, new KeyBinding(InputCode.KeyD, ctrl: true),
                enabled: () => hasPath && vMode),
            PopupMenuItem.Item("Delete", DeleteSelected, InputCode.KeyX,
                enabled: () => canDelete),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Select All", SelectAll, new KeyBinding(InputCode.KeyA, ctrl: true)),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Rename", BeginRename, InputCode.KeyF2,
                enabled: () => singleNode),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Flip Horizontal", () => FlipAxis(true),
                enabled: () => hasPath && vMode),
            PopupMenuItem.Item("Flip Vertical", () => FlipAxis(false),
                enabled: () => hasPath && vMode),
            PopupMenuItem.Item("Origin to Center", CenterShape, new KeyBinding(InputCode.KeyC, shift: true),
                enabled: () => hasPath && vMode),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Bring Forward", () => MovePathInOrder(-1), InputCode.KeyRightBracket,
                enabled: () => hasPath && vMode),
            PopupMenuItem.Item("Send Backward", () => MovePathInOrder(1), InputCode.KeyLeftBracket,
                enabled: () => hasPath && vMode),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Group", GroupSelected,
                enabled: () => hasPath && vMode),

            PopupMenuItem.Separator(),
            PopupMenuItem.Submenu("Boolean", showIcons: false),
            PopupMenuItem.Item("Union", BooleanUnion, new KeyBinding(InputCode.KeyU, ctrl: true, shift: true),
                level: 1, enabled: () => multiPath),
            PopupMenuItem.Item("Subtract", BooleanSubtract, new KeyBinding(InputCode.KeyD, ctrl: true, shift: true),
                level: 1, enabled: () => multiPath),
            PopupMenuItem.Item("Intersect", BooleanIntersect, new KeyBinding(InputCode.KeyI, ctrl: true, shift: true),
                level: 1, enabled: () => multiPath),
        };

        PopupMenu.Open(WidgetIds.ContextMenu, items.ToArray());
    }

    private void GroupSelected()
    {
        if (_selectedPaths.Count == 0) return;

        Undo.Record(Document);

        var layer = new SpriteGroup { Name = "Group" };

        // Insert the new layer at the position of the first selected path
        var firstPath = _selectedPaths[0];
        var parent = firstPath.Parent ?? Document.Root;
        var insertIndex = parent.Children.IndexOf(firstPath);
        if (insertIndex < 0) insertIndex = 0;
        parent.Insert(insertIndex, layer);

        // Move all selected paths into the new layer
        foreach (var path in _selectedPaths)
            path.RemoveFromParent();
        foreach (var path in _selectedPaths)
            layer.Add(path);

        // Select the new layer
        Document.Root.ClearSelection();
        Document.Root.ClearSelection();
        layer.IsSelected = true;
        layer.Expanded = true;
        RebuildSelectedPaths();
        MarkDirty();
    }

    public override void Update()
    {
        if (!Document.IsMutable)
        {
            // Immutable sprites just draw the image preview
            Document.DrawBounds(true);
            Document.Draw();
            return;
        }

        UpdateHandleCursor();
        UpdateAnimation();

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(5);
            Document.DrawOrigin();
            Graphics.SetSortGroup(4);
            DrawWireframe();
        }

        DrawGradientOverlay();

        UpdateMeshFromLayers();
        DrawMesh();

        DrawOnionSkin();

        if (Document.ShowSkeletonOverlay)
            DrawSkeletonOverlay();

        if (!Document.Edges.IsZero)
            DrawEdges();

        Document.DrawBounds();

        Mode?.Draw();
    }

    public override void LateUpdate()
    {
        if (!Document.IsMutable) return;
        Mode?.Update();
    }

    public override void UpdateUI() { }


    public override void UpdateOverlayUI()
    {
        if (!Document.IsMutable) return;

        using (FloatingToolbar.Begin())
        {
            FloatingToolbarUI();

            if (Document.AnimFrames.Count > 1)
            {
                FloatingToolbar.Row();
                DopeSheetUI();
            }
        }
    }

    private void FloatingToolbarUI()
    {
        // V/A mode toggles
        if (FloatingToolbar.Button(WidgetIds.VModeButton, EditorAssets.Sprites.IconMove, isSelected: CurrentMode == SpriteEditMode.Transform))
            SetMode(SpriteEditMode.Transform);

        if (FloatingToolbar.Button(WidgetIds.AModeButton, EditorAssets.Sprites.IconEdit, isSelected: CurrentMode == SpriteEditMode.Anchor))
            SetMode(SpriteEditMode.Anchor);

        if (FloatingToolbar.Button(WidgetIds.BevelModeButton, EditorAssets.Sprites.IconEdit, isSelected: CurrentMode == SpriteEditMode.Bevel))
            SetMode(SpriteEditMode.Bevel);

        FloatingToolbar.Divider();

        // Creation modes: Pen, Rect, Circle
        if (FloatingToolbar.Button(WidgetIds.PenToolButton, EditorAssets.Sprites.IconEdit, isSelected: CurrentMode == SpriteEditMode.Pen))
            SetMode(SpriteEditMode.Pen);

        if (FloatingToolbar.Button(WidgetIds.RectToolButton, EditorAssets.Sprites.IconLayer, isSelected: CurrentMode == SpriteEditMode.Rectangle))
            SetMode(SpriteEditMode.Rectangle);

        if (FloatingToolbar.Button(WidgetIds.CircleToolButton, EditorAssets.Sprites.IconCircle, isSelected: CurrentMode == SpriteEditMode.Circle))
            SetMode(SpriteEditMode.Circle);

        FloatingToolbar.Divider();

        // Add frame
        if (FloatingToolbar.Button(WidgetIds.AddFrameButton, EditorAssets.Sprites.IconKeyframe))
            InsertFrameAfter();

        FloatingToolbar.Divider();

        // Toggle group: Tile
        if (FloatingToolbar.Button(WidgetIds.TileButton, EditorAssets.Sprites.IconTiling, isSelected: Document.ShowTiling))
            Document.ShowTiling = !Document.ShowTiling;

        if (FloatingToolbar.Button(WidgetIds.OnionSkinButton, EditorAssets.Sprites.IconOnion, isSelected: _onionSkin))
            _onionSkin = !_onionSkin;
    }

    private int TotalTimeSlots() => Document.TotalTimeSlots;

    private void DopeSheetUI()
    {
        var maxSlots = Sprite.MaxFrames;
        var usedSlots = TotalTimeSlots();
        // Always show enough time blocks to fill the toolbar width (~5 blocks minimum)
        var blockCount = Math.Max((usedSlots + 3) / 4, 5);

        using (UI.BeginColumn(EditorStyle.Dopesheet.FloatingDopesheet))
        {
            // Header row: frame numbers
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

                // Fill remaining width to match toolbar
                UI.Flex();
            }

            // 1px gap (panel bg shows through)
            UI.Spacer(1);

            // Layer row: keyframe cells
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

                        // Keyframe cell with dot
                        using (UI.BeginContainer(isCurrentSlot
                            ? EditorStyle.Dopesheet.FloatingSelectedFrame
                            : EditorStyle.Dopesheet.FloatingFrame))
                        {
                            UI.Container(isCurrentSlot
                                ? EditorStyle.Dopesheet.FloatingSelectedFrameDot
                                : EditorStyle.Dopesheet.FloatingFrameDot);
                        }

                        slotIndex++;

                        // Hold cells (no dot, same color as keyframe)
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

                // Fill remaining width (panel bg shows through)
                UI.Flex();
            }
        }
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


    private void SetConstraint(Vector2Int? size)
    {
        Undo.Record(Document);
        Document.ConstrainedSize = size;
        MarkDirty();
        EditorUI.ClosePopup();
    }

    private void StrokeWidthButtonUI()
    {
        UI.DropDown(WidgetIds.StrokeWidth, () => [
            ..Enumerable.Range(1, 8).Select(i =>
                new PopupMenuItem { Label = Strings.Number(i), Handler = () => SetStrokeWidth((byte)i) }
            )
        ], Strings.Number(Document.CurrentStrokeWidth), EditorAssets.Sprites.IconStrokeSize);
    }


    public void SetCurrentTimeSlot(int timeSlot)
    {
        var maxSlots = Document.TotalTimeSlots;
        var newSlot = Math.Clamp(timeSlot, 0, maxSlots - 1);
        if (newSlot != _currentTimeSlot)
        {
            _currentTimeSlot = newSlot;
            ApplyCurrentFrameVisibility();
        }
    }

    private void ApplyCurrentFrameVisibility()
    {
        var fi = CurrentFrameIndex;
        if (fi < Document.AnimFrames.Count)
            Document.AnimFrames[fi].ApplyVisibility(Document.Root);
    }

    private void TogglePlayback()
    {
        _isPlaying = !_isPlaying;
        _playTimer = 0;
    }

    private void ToggleOnionSkin()
    {
        _onionSkin = !_onionSkin;
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

    private int TimeSlotForFrame(int frameIndex)
    {
        var slot = 0;
        for (var f = 0; f < frameIndex && f < Document.AnimFrames.Count; f++)
            slot += 1 + Document.AnimFrames[f].Hold;
        return slot;
    }

    private void InsertFrameBefore()
    {
        Undo.Record(Document);
        var fi = CurrentFrameIndex;
        var newFrame = Document.InsertFrame(fi);
        if (newFrame >= 0)
            SetCurrentTimeSlot(TimeSlotForFrame(newFrame));
        MarkDirty();
    }

    private void InsertFrameAfter()
    {
        Undo.Record(Document);
        var fi = CurrentFrameIndex;
        var newFrame = Document.InsertFrame(fi + 1);
        if (newFrame >= 0)
            SetCurrentTimeSlot(TimeSlotForFrame(newFrame));
        MarkDirty();
    }

    private void DeleteCurrentFrame()
    {
        if (Document.AnimFrames.Count <= 1) return;
        Undo.Record(Document);
        var fi = Document.DeleteFrame(CurrentFrameIndex);
        SetCurrentTimeSlot(TimeSlotForFrame(fi));
        MarkDirty();
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

    private void SetFillColor(Color32 color)
    {
        Document.CurrentFillColor = color;
        Document.CurrentFillType = SpriteFillType.Solid;

        if (_selectedPaths.Count == 0) return;
        foreach (var path in _selectedPaths)
        {
            path.FillColor = color;
            path.FillType = SpriteFillType.Solid;
        }
        _meshDirty = true;
    }

    private void SetFillGradient(SpriteFillType fillType, Color32 fallbackColor, SpriteFillGradient gradient)
    {
        Document.CurrentFillColor = fallbackColor;
        Document.CurrentFillType = fillType;
        Document.CurrentFillGradient = gradient;

        if (_selectedPaths.Count == 0) return;
        foreach (var path in _selectedPaths)
        {
            path.FillColor = fallbackColor;
            path.FillType = fillType;
            path.FillGradient = gradient;
            if (fillType == SpriteFillType.Linear && path.FillGradient.Start == Vector2.Zero && path.FillGradient.End == Vector2.Zero)
                path.InitializeDefaultGradient();
        }
        _meshDirty = true;
    }

    private void SetStrokeColor(Color32 color)
    {
        Document.CurrentStrokeColor = color;

        if (_selectedPaths.Count == 0) return;
        foreach (var path in _selectedPaths)
        {
            path.StrokeColor = color;
            path.StrokeWidth = Document.CurrentStrokeWidth;
            path.StrokeJoin = Document.CurrentStrokeJoin;
        }
        _meshDirty = true;
    }

    private void SetStrokeWidth(byte width)
    {
        Document.CurrentStrokeWidth = width;
        Undo.Record(Document);

        foreach (var path in _selectedPaths)
            path.StrokeWidth = width;

        InvalidateMesh();
    }

    private void SetStrokeJoin(SpriteStrokeJoin join)
    {
        Document.CurrentStrokeJoin = join;
        Undo.Record(Document);

        foreach (var path in _selectedPaths)
            path.StrokeJoin = join;

        InvalidateMesh();
    }

    private void CycleSpritePathOperation()
    {
        var newOp = Document.CurrentOperation switch
        {
            SpritePathOperation.Normal => SpritePathOperation.Subtract,
            SpritePathOperation.Subtract => SpritePathOperation.Clip,
            _ => SpritePathOperation.Normal,
        };

        if (_selectedPaths.Count > 0)
        {
            Undo.Record(Document);
            Document.CurrentOperation = newOp;
            foreach (var path in _selectedPaths)
                path.Operation = newOp;

            InvalidateMesh();
        }
        else
        {
            Document.CurrentOperation = newOp;
        }
    }

    private void SetSpritePathOperation(SpritePathOperation operation)
    {
        Undo.Record(Document);
        Document.CurrentOperation = operation;

        foreach (var path in _selectedPaths)
            path.Operation = operation;

        InvalidateMesh();
    }

    private void CenterShape()
    {
        Undo.Record(Document);

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var hasAnchors = false;

        Document.Root.ForEachEditablePath(p =>
        {
            if (p.TotalAnchorCount == 0) return;
            hasAnchors = true;
            p.UpdateSamples();
            p.UpdateBounds();
            var b = p.Bounds;
            min = Vector2.Min(min, new Vector2(b.X, b.Y));
            max = Vector2.Max(max, new Vector2(b.Right, b.Bottom));
        });

        if (hasAnchors)
        {
            var dpi = EditorApplication.Config.PixelsPerUnit;
            var invDpi = 1f / dpi;
            var centerWorld = (min + max) * 0.5f;
            var center = new Vector2(
                MathF.Round(centerWorld.X * dpi) * invDpi,
                MathF.Round(centerWorld.Y * dpi) * invDpi);

            // Translate all paths to center origin
            Document.Root.ForEachEditablePath(p =>
            {
                foreach (var contour in p.Contours)
                {
                    for (var i = 0; i < contour.Anchors.Count; i++)
                    {
                        var a = contour.Anchors[i];
                        a.Position -= center;
                        contour.Anchors[i] = a;
                    }
                }
                p.MarkDirty();
            });
        }

        MarkDirty();
    }

    private void FlipAxis(bool horizontal)
    {
        if (_selectedPaths.Count == 0) return;

        Undo.Record(Document);
        foreach (var path in _selectedPaths)
        {
            var oldCenter = path.LocalBounds.Center;

            if (path.HasTransform && Matrix3x2.Invert(path.PathTransform, out var inverse))
            {
                // Flip in world space so the result matches what the user sees
                var oldTransform = path.PathTransform;
                var worldCenter = path.Bounds.Center;

                foreach (var contour in path.Contours)
                {
                    for (var i = 0; i < contour.Anchors.Count; i++)
                    {
                        var a = contour.Anchors[i];
                        var world = Vector2.Transform(a.Position, oldTransform);
                        world = horizontal
                            ? new Vector2(2 * worldCenter.X - world.X, world.Y)
                            : new Vector2(world.X, 2 * worldCenter.Y - world.Y);
                        a.Position = Vector2.Transform(world, inverse);
                        a.Curve = -a.Curve;
                        contour.Anchors[i] = a;
                    }
                }
            }
            else
            {
                // No transform: flip in local space (local = world)
                var center = path.LocalBounds.Center;
                foreach (var contour in path.Contours)
                {
                    for (var i = 0; i < contour.Anchors.Count; i++)
                    {
                        var a = contour.Anchors[i];
                        a.Position = horizontal
                            ? new Vector2(2 * center.X - a.Position.X, a.Position.Y)
                            : new Vector2(a.Position.X, 2 * center.Y - a.Position.Y);
                        a.Curve = -a.Curve;
                        contour.Anchors[i] = a;
                    }
                }
            }

            path.MarkDirty();
            path.UpdateSamples();
            path.UpdateBounds();
            path.CompensateTranslation(oldCenter);
        }
        MarkDirty();
    }

    #region Boolean Operations

    private void BooleanUnion() => BooleanOp(Clipper2Lib.ClipType.Union);
    private void BooleanSubtract() => BooleanOp(Clipper2Lib.ClipType.Difference);
    private void BooleanIntersect() => BooleanOp(Clipper2Lib.ClipType.Intersection);

    private void BooleanOp(Clipper2Lib.ClipType clipType)
    {
        if (_selectedPaths.Count < 2) return;

        var subject = _selectedPaths[0].GetClipperPaths();
        if (subject.Count == 0) return;

        // Accumulate clip paths from remaining selected paths
        var clip = new Clipper2Lib.PathsD();
        for (var i = 1; i < _selectedPaths.Count; i++)
        {
            var paths = _selectedPaths[i].GetClipperPaths();
            clip.AddRange(paths);
        }
        if (clip.Count == 0) return;

        // Run the boolean operation
        var result = Clipper2Lib.Clipper.BooleanOp(clipType,
            subject, clip, Clipper2Lib.FillRule.NonZero, precision: 6);
        if (result.Count == 0) return;

        Undo.Record(Document);

        // Create result path inheriting properties from the first selected path
        var firstPath = _selectedPaths[0];
        var resultPath = SpritePathClipper.PathsToSpritePath(
            result, firstPath.FillColor, firstPath.StrokeColor, firstPath.StrokeWidth, strokeJoin: firstPath.StrokeJoin);

        // Insert result into the first path's parent at its position
        var parent = firstPath.Parent ?? Document.Root;
        var insertIndex = parent.Children.IndexOf(firstPath);

        // Remove all original paths
        foreach (var path in _selectedPaths)
            path.RemoveFromParent();

        // Insert result
        if (insertIndex >= 0 && insertIndex <= parent.Children.Count)
            parent.Insert(insertIndex, resultPath);
        else
            parent.Add(resultPath);

        resultPath.SelectPath();
        resultPath.UpdateSamples();
        resultPath.UpdateBounds();
        MarkDirty();
        RebuildSelectedPaths();
    }

    #endregion

    private void MovePathInOrder(int direction)
    {
        if (_selectedPaths.Count == 0) return;

        // Group selected paths by parent layer, constrain moves within each layer
        var moved = false;
        Undo.Record(Document);

        // Process in correct order to avoid index conflicts:
        // moving forward (direction > 0) = process from end, backward = process from start
        var ordered = direction > 0
            ? _selectedPaths.OrderByDescending(p => p.Parent?.Children.IndexOf(p) ?? -1)
            : _selectedPaths.OrderBy(p => p.Parent?.Children.IndexOf(p) ?? -1);

        foreach (var path in ordered)
        {
            var parent = path.Parent;
            if (parent == null) continue;

            var idx = parent.Children.IndexOf(path);
            var newIdx = idx + direction;
            if (idx < 0 || newIdx < 0 || newIdx >= parent.Children.Count) continue;

            parent.RemoveAt(idx);
            parent.Insert(newIdx, path);
            moved = true;
        }

        if (!moved)
            Undo.Cancel();
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

    private static readonly List<SpritePath> _pathHitResults = [];
    private static readonly List<SpritePath> _hitPaths = [];

    internal bool HandlePathClick(Vector2 localMousePos, bool shift)
    {
        _pathHitResults.Clear();
        Document.Root.HitTestPath(localMousePos, _pathHitResults);

        HitPaths.Clear();
        foreach (var p in _pathHitResults)
            HitPaths.Add(p);

        if (HitPaths.Count == 0)
            return false;

        // Viewport path clicks are mutually exclusive with layer selection
        Document.Root.ClearSelection();

        if (!shift)
        {
            // Without shift: select topmost, or cycle if already selected
            var topmost = HitPaths[0];
            if (topmost.IsSelected)
            {
                // Cycle: find next unselected, or wrap
                SpritePath? next = null;
                for (var i = 1; i < HitPaths.Count; i++)
                {
                    if (!HitPaths[i].IsSelected)
                    {
                        next = HitPaths[i];
                        break;
                    }
                }

                Document.Root.ClearSelection();
                (next ?? HitPaths[0]).SelectPath();
            }
            else
            {
                Document.Root.ClearSelection();
                topmost.SelectPath();
            }
        }
        else
        {
            // Shift: add the next unselected path to selection
            SpritePath? nextUnselected = null;
            foreach (var p in HitPaths)
            {
                if (!p.IsSelected)
                {
                    nextUnselected = p;
                    break;
                }
            }

            if (nextUnselected != null)
            {
                nextUnselected.SelectPath();
            }
            else if (HitPaths.Count > 0)
            {
                // All selected — wrap: deselect topmost, it cycles visually
                HitPaths[0].DeselectPath();
            }
        }

        RebuildSelectedPaths();
        return true;
    }

    private void DrawWireframe()
    {
        if (_selectedPaths.Count == 0) return;

        // When gradient handles are active, hide normal wireframe controls
        if (IsGradientOverlayVisible()) return;

        var transform = Document.Transform;

        if (CurrentMode != SpriteEditMode.Transform)
        {
            // Anchor-based modes: draw anchors and edges for selected paths
            foreach (var path in _selectedPaths)
            {
                if (path.TotalAnchorCount < 2) continue;
                path.UpdateSamples();
                var localTransform = path.HasTransform ? path.PathTransform : Matrix3x2.Identity;
                DrawPathSegments(path, localTransform, transform);
                DrawPathAnchors(path, localTransform, transform);
            }
        }
        else
        {
            // V mode: draw path outlines and bounding box around each selected path
            DrawSelectedPathOutlines(transform);
            DrawSelectionBounds(transform);
        }
    }

    private void UpdateHandleCursor()
    {
        _hoverPath = null;
        _hoverAnchorIndex = -1;
        _hoverHandle = SpritePathHandle.None;

        if (_selectedPaths.Count == 0 || IsGradientOverlayVisible())
        {
            SetCursor(SpritePathHandle.None);
            return;
        }

        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        if (CurrentMode == SpriteEditMode.Transform)
        {
            _hoverHandle = HitTestHandles(localMousePos);
            SetCursor(_hoverHandle);
        }
        else if (CurrentMode == SpriteEditMode.Anchor || CurrentMode == SpriteEditMode.Bevel)
        {
            if (Input.IsAltDown(InputScope.All))
            {
                var segHit = Document.Root.HitTestSegment(localMousePos, onlySelected: true);
                if (segHit.HasValue)
                {
                    EditorCursor.SetCrosshair();
                    return;
                }
            }

            var hit = Document.Root.HitTestAnchor(localMousePos, onlySelected: true);
            if (hit.HasValue)
            {
                _hoverPath = hit.Value.Path;
                _hoverAnchorIndex = hit.Value.AnchorIndex;
                SetCursor(SpritePathHandle.Move);
                return;
            }
            else
                SetCursor(SpritePathHandle.None);
        }
        else
            SetCursor(SpritePathHandle.None);
    }

    private void DrawSelectedPathOutlines(Matrix3x2 docTransform)
    {
        var lineWidth = EditorStyle.SpritePath.SegmentLineWidth * 0.5f;

        using var _ = Gizmos.PushState(EditorLayer.DocumentEditor);
        Graphics.SetTransform(docTransform);
        Gizmos.SetColor(EditorStyle.Palette.Primary);

        foreach (var path in _selectedPaths)
        {
            if (path.TotalAnchorCount < 2) continue;
            path.UpdateSamples();
            var localTransform = path.HasTransform ? path.PathTransform : Matrix3x2.Identity;

            for (var ci = 0; ci < path.Contours.Count; ci++)
            {
                var contour = path.Contours[ci];
                var segmentCount = contour.Open ? contour.Anchors.Count - 1 : contour.Anchors.Count;
                for (var i = 0; i < segmentCount; i++)
                    DrawContourSegment(contour, i, localTransform, lineWidth, 1);
            }
        }
    }

    private void DrawSelectionBounds(Matrix3x2 transform)
    {
        if (_selectedPaths.Count == 0) return;

        UpdateSelectionBounds();

        var bounds = _selectionLocalBounds;
        if (bounds.Width <= 0 && bounds.Height <= 0) return;

        using var _ = Gizmos.PushState(EditorLayer.DocumentEditor);

        // Draw the oriented bounding box
        var selToDoc = Matrix3x2.CreateRotation(_selectionRotation);
        Graphics.SetTransform(selToDoc * transform);

        Gizmos.SetColor(EditorStyle.Palette.Primary);
        var lineWidth = EditorStyle.SpritePath.SegmentLineWidth;
        var tl = new Vector2(bounds.X, bounds.Y);
        var tr = new Vector2(bounds.Right, bounds.Y);
        var br = new Vector2(bounds.Right, bounds.Bottom);
        var bl = new Vector2(bounds.X, bounds.Bottom);
        Gizmos.DrawLine(tl, tr, lineWidth, order: 2);
        Gizmos.DrawLine(tr, br, lineWidth, order: 2);
        Gizmos.DrawLine(br, bl, lineWidth, order: 2);
        Gizmos.DrawLine(bl, tl, lineWidth, order: 2);

        // Draw handles at corners and edge midpoints (in selection space)
        var midX = bounds.X + bounds.Width * 0.5f;
        var midY = bounds.Y + bounds.Height * 0.5f;

        var h = _hoverHandle;
        Gizmos.DrawAnchor(tl, selected: h is SpritePathHandle.ScaleTopLeft, order: 6);
        Gizmos.DrawAnchor(tr, selected: h is SpritePathHandle.ScaleTopRight, order: 6);
        Gizmos.DrawAnchor(br, selected: h is SpritePathHandle.ScaleBottomRight, order: 6);
        Gizmos.DrawAnchor(bl, selected: h is SpritePathHandle.ScaleBottomLeft, order: 6);
        Gizmos.DrawAnchor(new Vector2(midX, bounds.Y), selected: h is SpritePathHandle.ScaleTop, order: 6);
        Gizmos.DrawAnchor(new Vector2(midX, bounds.Bottom), selected: h is SpritePathHandle.ScaleBottom, order: 6);
        Gizmos.DrawAnchor(new Vector2(bounds.X, midY), selected: h is SpritePathHandle.ScaleLeft, order: 6);
        Gizmos.DrawAnchor(new Vector2(bounds.Right, midY), selected: h is SpritePathHandle.ScaleRight, order: 6);

        // Draw rotation handles at offset positions outside corners
        var center = new Vector2(midX, midY);
        var rotScale = EditorStyle.SpritePath.RotateHandleScale;
        Gizmos.DrawAnchor(GetRotateHandleOffset(tl, center), selected: h is SpritePathHandle.RotateTopLeft, scale: rotScale, order: 6);
        Gizmos.DrawAnchor(GetRotateHandleOffset(tr, center), selected: h is SpritePathHandle.RotateTopRight, scale: rotScale, order: 6);
        Gizmos.DrawAnchor(GetRotateHandleOffset(br, center), selected: h is SpritePathHandle.RotateBottomRight, scale: rotScale, order: 6);
        Gizmos.DrawAnchor(GetRotateHandleOffset(bl, center), selected: h is SpritePathHandle.RotateBottomLeft, scale: rotScale, order: 6);
    }

    private static Vector2 GetRotateHandleOffset(Vector2 corner, Vector2 boundsCenter)
    {
        var dir = Vector2.Normalize(corner - boundsCenter);
        var offset = EditorStyle.SpritePath.RotateHandleOffset * Gizmos.ZoomRefScale;
        return corner + dir * offset;
    }

    internal static bool IsScaleHandle(SpritePathHandle hit) => hit >= SpritePathHandle.ScaleTopLeft && hit <= SpritePathHandle.ScaleLeft;
    internal static bool IsRotateHandle(SpritePathHandle hit) => hit >= SpritePathHandle.RotateTopLeft && hit <= SpritePathHandle.RotateBottomLeft;

    // Hit test handles in selection-rotated space
    internal SpritePathHandle HitTestHandles(Vector2 docLocalPos)
    {
        var bounds = _selectionLocalBounds;
        if (bounds.Width <= 0 && bounds.Height <= 0) return SpritePathHandle.None;

        // Transform from document-local to selection-local space
        var selPos = Vector2.Transform(docLocalPos, Matrix3x2.CreateRotation(-_selectionRotation));

        var hitRadius = EditorStyle.SpritePath.AnchorHitRadius;
        var hitRadiusSqr = hitRadius * hitRadius;

        var midX = bounds.X + bounds.Width * 0.5f;
        var midY = bounds.Y + bounds.Height * 0.5f;

        // Corner positions in selection space
        Span<Vector2> corners = stackalloc Vector2[4];
        corners[0] = new Vector2(bounds.X, bounds.Y);
        corners[1] = new Vector2(bounds.Right, bounds.Y);
        corners[2] = new Vector2(bounds.Right, bounds.Bottom);
        corners[3] = new Vector2(bounds.X, bounds.Bottom);

        // Test rotation handles at offset positions outside corners
        var boundsCenter = new Vector2(midX, midY);
        var rotateHitRadius = hitRadius * EditorStyle.SpritePath.RotateHandleScale;
        var rotateHitRadiusSqr = rotateHitRadius * rotateHitRadius;
        for (var i = 0; i < 4; i++)
        {
            var rotPos = GetRotateHandleOffset(corners[i], boundsCenter);
            if (Vector2.DistanceSquared(selPos, rotPos) <= rotateHitRadiusSqr)
                return SpritePathHandle.RotateTopLeft + i;
        }

        // Test corner scale handles
        for (var i = 0; i < 4; i++)
        {
            if (Vector2.DistanceSquared(selPos, corners[i]) <= hitRadiusSqr)
                return SpritePathHandle.ScaleTopLeft + i * 2;
        }

        // Edge midpoint handles
        Span<Vector2> edges = stackalloc Vector2[4];
        edges[0] = new Vector2(midX, bounds.Y);
        edges[1] = new Vector2(bounds.Right, midY);
        edges[2] = new Vector2(midX, bounds.Bottom);
        edges[3] = new Vector2(bounds.X, midY);

        Span<SpritePathHandle> edgeHits = stackalloc SpritePathHandle[4];
        edgeHits[0] = SpritePathHandle.ScaleTop;
        edgeHits[1] = SpritePathHandle.ScaleRight;
        edgeHits[2] = SpritePathHandle.ScaleBottom;
        edgeHits[3] = SpritePathHandle.ScaleLeft;

        for (var i = 0; i < 4; i++)
        {
            if (Vector2.DistanceSquared(selPos, edges[i]) <= hitRadiusSqr)
                return edgeHits[i];
        }

        // Inside oriented bbox = move (test in selection space using axis-aligned check)
        if (bounds.Contains(selPos))
            return SpritePathHandle.Move;

        return SpritePathHandle.None;
    }

    private void SetCursor(SpritePathHandle hit)
    {
        if (hit == SpritePathHandle.None)
            EditorCursor.SetArrow();
        else if (hit == SpritePathHandle.Move)
            EditorCursor.SetMove();
        else if (hit is SpritePathHandle.RotateTopLeft or SpritePathHandle.RotateTopRight or
                 SpritePathHandle.RotateBottomRight or SpritePathHandle.RotateBottomLeft)
            EditorCursor.SetRotate(hit, _selectionRotation);
        else
            EditorCursor.SetScale(hit, _selectionRotation);
    }

    // Get the handle position in selection-local space
    private Vector2 GetHandlePositionInSelSpace(SpritePathHandle hit)
    {
        var b = _selectionLocalBounds;
        var midX = b.X + b.Width * 0.5f;
        var midY = b.Y + b.Height * 0.5f;

        return hit switch
        {
            SpritePathHandle.ScaleTopLeft => new Vector2(b.X, b.Y),
            SpritePathHandle.ScaleTop => new Vector2(midX, b.Y),
            SpritePathHandle.ScaleTopRight => new Vector2(b.Right, b.Y),
            SpritePathHandle.ScaleRight => new Vector2(b.Right, midY),
            SpritePathHandle.ScaleBottomRight => new Vector2(b.Right, b.Bottom),
            SpritePathHandle.ScaleBottom => new Vector2(midX, b.Bottom),
            SpritePathHandle.ScaleBottomLeft => new Vector2(b.X, b.Bottom),
            SpritePathHandle.ScaleLeft => new Vector2(b.X, midY),
            _ => b.Center,
        };
    }

    // Get the pivot (opposite handle) in selection-local space
    internal Vector2 GetOppositePivotInSelSpace(SpritePathHandle hit)
    {
        var b = _selectionLocalBounds;
        var midX = b.X + b.Width * 0.5f;
        var midY = b.Y + b.Height * 0.5f;

        return hit switch
        {
            SpritePathHandle.ScaleTopLeft => new Vector2(b.Right, b.Bottom),
            SpritePathHandle.ScaleTop => new Vector2(midX, b.Bottom),
            SpritePathHandle.ScaleTopRight => new Vector2(b.X, b.Bottom),
            SpritePathHandle.ScaleRight => new Vector2(b.X, midY),
            SpritePathHandle.ScaleBottomRight => new Vector2(b.X, b.Y),
            SpritePathHandle.ScaleBottom => new Vector2(midX, b.Y),
            SpritePathHandle.ScaleBottomLeft => new Vector2(b.Right, b.Y),
            SpritePathHandle.ScaleLeft => new Vector2(b.Right, midY),
            _ => b.Center,
        };
    }

    private void DrawEdges()
    {
        var bounds = Document.Bounds;
        var edges = Document.Edges;
        var ppu = 1.0f / EditorApplication.Config.PixelsPerUnit;

        var edgeL = edges.L * ppu;
        var edgeR = edges.R * ppu;
        var edgeT = edges.T * ppu;
        var edgeB = edges.B * ppu;

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(6);
            Gizmos.SetColor(new Color(0.2f, 0.9f, 0.2f, 0.8f));

            var lineWidth = EditorStyle.Workspace.DocumentBoundsLineWidth;

            if (edgeT > 0)
            {
                var y = bounds.Top + edgeT;
                Gizmos.DrawLine(new Vector2(bounds.Left, y), new Vector2(bounds.Right, y), lineWidth);
            }

            if (edgeB > 0)
            {
                var y = bounds.Bottom - edgeB;
                Gizmos.DrawLine(new Vector2(bounds.Left, y), new Vector2(bounds.Right, y), lineWidth);
            }

            if (edgeL > 0)
            {
                var x = bounds.Left + edgeL;
                Gizmos.DrawLine(new Vector2(x, bounds.Top), new Vector2(x, bounds.Bottom), lineWidth);
            }

            if (edgeR > 0)
            {
                var x = bounds.Right - edgeR;
                Gizmos.DrawLine(new Vector2(x, bounds.Top), new Vector2(x, bounds.Bottom), lineWidth);
            }
        }
    }

    private void DrawSkeletonOverlay()
    {
        var skeleton = Document.Skeleton.Value;
        if (skeleton == null)
            return;

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(0);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            foreach (var bound in skeleton.Attachments)
            {
                if (bound is not SpriteDocument sprite || sprite == Document) continue;
                Graphics.SetBlendMode(BlendMode.Alpha);
                Graphics.SetTransform(Document.Transform);
                sprite.DrawSprite(alpha: 0.3f);
            }
        }

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetSortGroup(6);
            Graphics.SetTransform(Document.Transform);

            for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
                Gizmos.DrawBoneAndJoints(skeleton, boneIndex, selected: false);
        }
    }

    private void SpriteInspectorUI()
    {
        using var _ = Inspector.BeginSection("SPRITE");
        if (Inspector.IsSectionCollapsed) return;

        if (!Document.IsReference)
        {
            using (Inspector.BeginProperty("Export"))
            {
                var shouldExport = Document.ShouldExport;
                if (UI.Toggle(WidgetIds.ExportToggle, "", shouldExport, EditorStyle.Inspector.Toggle, EditorAssets.Sprites.IconCheck))
                {
                    shouldExport = !shouldExport;
                    Undo.Record(Document);
                    Document.ShouldExport = shouldExport;
                    AssetManifest.IsModified = true;
                }
            }
        }

        using (Inspector.BeginProperty("Size"))
        {
            var sizes = EditorApplication.Config.SpriteSizes;
            var constraintLabel = "Auto";
            if (Document.ConstrainedSize.HasValue)
                for (int i = 0; i < sizes.Length; i++)
                    if (Document.ConstrainedSize.Value == sizes[i].Size)
                    {
                        constraintLabel = sizes[i].Label;
                        break;
                    }

            UI.DropDown(WidgetIds.ConstraintDropDown, () => [
                ..EditorApplication.Config.SpriteSizes.Select(s =>
                new PopupMenuItem { Label = s.Label, Handler = () => SetConstraint(s.Size) }
            ),
            new PopupMenuItem { Label = "Auto", Handler = () => SetConstraint(null)}
            ], constraintLabel, EditorAssets.Sprites.IconConstraint);
        }

        using (Inspector.BeginProperty("Sort Order"))
        {
            EditorUI.SortOrderDropDown(WidgetIds.SortOrder, Document.SortOrderId, id =>
            {
                Undo.Record(Document);
                Document.SortOrderId = id;
            });
        }

        // Skeleton
        using (Inspector.BeginProperty("Skeleton"))
        {
            var skeletonLabel = Document.Skeleton.IsResolved
                ? StringId.Get(Document.Skeleton.Value!.Name).ToString()
                : "None";

            UI.DropDown(WidgetIds.SkeletonDropDown, () =>
            {
                var skeletonItems = new List<PopupMenuItem>();

                foreach (var doc in DocumentManager.Documents)
                {
                    if (doc is not SkeletonDocument skeleton || skeleton.BoneCount == 0)
                        continue;

                    var name = StringId.Get(skeleton.Name).ToString();
                    skeletonItems.Add(new PopupMenuItem { Label = name, Handler = () => CommitSkeletonBinding(skeleton) });
                }

                skeletonItems.Add(new PopupMenuItem { Label = "None", Handler = ClearSkeletonBinding });
                return [.. skeletonItems];
            }, skeletonLabel, EditorAssets.Sprites.IconBone);
        }

        if (Document.Skeleton.IsResolved)
        {
            using (Inspector.BeginProperty("Bone"))
            {
                var skeleton = Document.Skeleton.Value!;
                var boneLabel = Document.BoneName ?? "None";

                UI.DropDown(WidgetIds.BoneDropDown, () =>
                {
                    var boneItems = new List<PopupMenuItem>();

                    for (var i = 0; i < skeleton.BoneCount; i++)
                    {
                        var boneName = skeleton.Bones[i].Name;
                        boneItems.Add(new PopupMenuItem { Label = boneName, Handler = () => CommitBoneBinding(boneName) });
                    }

                    boneItems.Add(new PopupMenuItem { Label = "None", Handler = () => CommitBoneBinding(null) });
                    return [.. boneItems];
                }, boneLabel, EditorAssets.Sprites.IconBone);
            }

            if (UI.Button(WidgetIds.ShowInSkeleton, EditorAssets.Sprites.IconPreview, EditorStyle.Button.ToggleIcon, isSelected: Document.ShowInSkeleton))
            {
                Undo.Record(Document);
                Document.ShowInSkeleton = !Document.ShowInSkeleton;
                Document.Skeleton.Value?.UpdateSprites();
            }

            if (UI.Button(WidgetIds.ShowSkeletonOverlay, EditorAssets.Sprites.IconBone, EditorStyle.Button.ToggleIcon, isSelected: Document.ShowSkeletonOverlay))
            {
                Undo.Record(Document);
                Document.ShowSkeletonOverlay = !Document.ShowSkeletonOverlay;
            }
        }
    }

    private void PathInspectorUI()
    {
        if (!HasPathSelection || HasLayerSelection)
            return;

        using (Inspector.BeginSection("PATH"))
        {
            if (Inspector.IsSectionCollapsed)
                return;

            using (Inspector.BeginProperty("Operation"))
            using (UI.BeginRow(EditorStyle.Control.Spacing))
            {
                if (UI.Button(WidgetIds.PathNormal, EditorAssets.Sprites.IconFill, EditorStyle.Button.ToggleIcon, isSelected: Document.CurrentOperation == SpritePathOperation.Normal))
                    SetSpritePathOperation(SpritePathOperation.Normal);

                if (UI.Button(WidgetIds.PathSubtract, EditorAssets.Sprites.IconSubtract, EditorStyle.Button.ToggleIcon, isSelected: Document.CurrentOperation == SpritePathOperation.Subtract))
                    SetSpritePathOperation(SpritePathOperation.Subtract);

                if (UI.Button(WidgetIds.PathClip, EditorAssets.Sprites.IconClip, EditorStyle.Button.ToggleIcon, isSelected: Document.CurrentOperation == SpritePathOperation.Clip))
                    SetSpritePathOperation(SpritePathOperation.Clip);
            }

            if (_selectedPaths.Count >= 2)
            {
                using (Inspector.BeginProperty("Boolean"))
                using (UI.BeginRow(EditorStyle.Control.Spacing))
                {
                    if (UI.Button(WidgetIds.BoolUnion, "Union", EditorStyle.Button.Secondary))
                        BooleanUnion();
                    if (UI.Button(WidgetIds.BoolSubtract, "Subtract", EditorStyle.Button.Secondary))
                        BooleanSubtract();
                    if (UI.Button(WidgetIds.BoolIntersect, "Intersect", EditorStyle.Button.Secondary))
                        BooleanIntersect();
                }
            }

            if (Document.CurrentOperation != SpritePathOperation.Subtract)
            {
                using (Inspector.BeginProperty("Fill"))
                {
                    var wasPickerOpen = ColorPicker.IsOpen(WidgetIds.FillColor);
                    var fillColor = Document.CurrentFillColor.ToColor();
                    EditorUI.ColorButton(WidgetIds.FillColor, ref fillColor);

                    // Re-open with gradient state when the picker just opened on a gradient path
                    if (!wasPickerOpen && ColorPicker.IsOpen(WidgetIds.FillColor) && Document.CurrentFillType == SpriteFillType.Linear)
                    {
                        Log.Info($"[Gradient] Re-opening picker: start={Document.CurrentFillGradient.StartColor} end={Document.CurrentFillGradient.EndColor}");
                        ColorPicker.Open(WidgetIds.FillColor, Document.CurrentFillType,
                            Document.CurrentFillColor, Document.CurrentFillGradient);
                    }

                    // Set gradient handle passthrough while picker is in gradient mode
                    ColorPicker.OnBackdropClick = IsGradientOverlayVisible() ? OnGradientBackdropClick : null;

                    // Sync color/gradient to paths while picker is open
                    if (ColorPicker.IsOpen(WidgetIds.FillColor))
                    {
                        var changed = false;
                        if (ColorPicker.ResultFillType == SpriteFillType.Linear)
                        {
                            var pickerGrad = ColorPicker.ResultGradient;
                            if (_selectedPaths.Count > 0)
                            {
                                var cur = _selectedPaths[0].FillGradient;
                                changed = cur.StartColor != pickerGrad.StartColor || cur.EndColor != pickerGrad.EndColor;
                            }
                            Document.CurrentFillType = SpriteFillType.Linear;
                            var fallback = pickerGrad.StartColor.A > 0 ? pickerGrad.StartColor : pickerGrad.EndColor;
                            if (fallback.A == 0) fallback = new Color32(fallback.R, fallback.G, fallback.B, 255);
                            Document.CurrentFillColor = fallback;
                            foreach (var path in _selectedPaths)
                            {
                                path.FillType = SpriteFillType.Linear;
                                path.FillColor = fallback;
                                var g = path.FillGradient;
                                g.StartColor = pickerGrad.StartColor;
                                g.EndColor = pickerGrad.EndColor;
                                path.FillGradient = g;
                                if (g.Start == Vector2.Zero && g.End == Vector2.Zero)
                                    path.InitializeDefaultGradient();
                            }
                            if (_selectedPaths.Count > 0)
                                Document.CurrentFillGradient = _selectedPaths[0].FillGradient;
                        }
                        else
                        {
                            var newColor = fillColor.ToColor32();
                            changed = newColor != Document.CurrentFillColor;
                            SetFillColor(newColor);
                        }

                        if (changed)
                            UI.NotifyChanged(fillColor.GetHashCode());
                        _meshDirty = true;
                    }

                    // Handle undo — must be after NotifyChanged so WasChanged() sees it
                    UI.HandleChange(Document);
                }

                using (Inspector.BeginProperty("Stroke"))
                using (UI.BeginRow(EditorStyle.Control.Spacing))
                {
                    var strokeColor = Document.CurrentStrokeColor.ToColor();
                    EditorUI.ColorButton(WidgetIds.StrokeColor, ref strokeColor);

                    if (ColorPicker.IsOpen(WidgetIds.StrokeColor))
                    {
                        var newColor = strokeColor.ToColor32();
                        if (newColor != Document.CurrentStrokeColor)
                            UI.NotifyChanged(strokeColor.GetHashCode());
                        SetStrokeColor(newColor);
                    }

                    UI.HandleChange(Document);

                    if (strokeColor.A > 0)
                        StrokeWidthButtonUI();
                }

                if (Document.CurrentStrokeColor.A > 0)
                {
                    using (Inspector.BeginProperty("Join"))
                    using (UI.BeginRow(EditorStyle.Control.Spacing))
                    {
                        if (UI.Button(WidgetIds.StrokeJoinRound, EditorAssets.Sprites.IconStrokeJoinRound, EditorStyle.Button.ToggleIcon, isSelected: Document.CurrentStrokeJoin == SpriteStrokeJoin.Round))
                            SetStrokeJoin(SpriteStrokeJoin.Round);

                        if (UI.Button(WidgetIds.StrokeJoinMiter, EditorAssets.Sprites.IconStrokeJoinMiter, EditorStyle.Button.ToggleIcon, isSelected: Document.CurrentStrokeJoin == SpriteStrokeJoin.Miter))
                            SetStrokeJoin(SpriteStrokeJoin.Miter);

                        if (UI.Button(WidgetIds.StrokeJoinBevel, EditorAssets.Sprites.IconStrokeJoinBevel, EditorStyle.Button.ToggleIcon, isSelected: Document.CurrentStrokeJoin == SpriteStrokeJoin.Bevel))
                            SetStrokeJoin(SpriteStrokeJoin.Bevel);
                    }
                }
            }
        }
    }

    public override void InspectorUI()
    {
        if (!HasPathSelection && !HasLayerSelection)
            SpriteInspectorUI();

        if (Document.IsMutable)
        {
            EdgesInspectorUI();
            PathInspectorUI();
        }
    }

    #region Edges

    private static bool EdgeIntInput(WidgetId id, float value, out float newValue)
    {
        var text = ((int)value).ToString();
        newValue = value;
        using (UI.BeginFlex())
        {
            var result = UI.TextInput(id, text, EditorStyle.Inspector.TextBox, "0");
            if (result != text && int.TryParse(result, out var parsed) && parsed >= 0)
            {
                newValue = parsed;
                return true;
            }
        }
        return false;
    }

    private void EdgesInspectorUI()
    {
        if (Document.Edges.IsZero)
        {
            static void EmptySectionContent()
            {
                ElementTree.BeginAlign(Align.Min, Align.Center);
                if (UI.Button(WidgetIds.AddEdgesButton, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
                {
                    var doc = (Workspace.ActiveDocument as SpriteDocument)!;
                    Undo.Record(doc);
                    doc.Edges = new EdgeInsets(8);
                    doc.UpdateBounds();
                }
                ElementTree.EndAlign();
            }

            using (Inspector.BeginSection("EDGES", content: EmptySectionContent, empty: true))
            return;
        }

        static void EdgesSectionContent()
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(WidgetIds.RemoveEdgesButton, EditorAssets.Sprites.IconDelete, EditorStyle.Inspector.SectionButton))
            {
                var doc = (Workspace.ActiveDocument as SpriteDocument)!;
                Undo.Record(doc);
                doc.Edges = EdgeInsets.Zero;
            }
            ElementTree.EndAlign();
        }

        using (Inspector.BeginSection("EDGES", content: EdgesSectionContent))
        {
            if (Inspector.IsSectionCollapsed) return;

            var edges = Document.Edges;
            var changed = false;

            using (Inspector.BeginProperty("Top"))
                if (EdgeIntInput(WidgetIds.EdgeTop, edges.T, out var v)) { edges = new EdgeInsets(v, edges.L, edges.B, edges.R); changed = true; }

            using (Inspector.BeginProperty("Left"))
                if (EdgeIntInput(WidgetIds.EdgeLeft, edges.L, out var v)) { edges = new EdgeInsets(edges.T, v, edges.B, edges.R); changed = true; }

            using (Inspector.BeginProperty("Bottom"))
                if (EdgeIntInput(WidgetIds.EdgeBottom, edges.B, out var v)) { edges = new EdgeInsets(edges.T, edges.L, v, edges.R); changed = true; }

            using (Inspector.BeginProperty("Right"))
                if (EdgeIntInput(WidgetIds.EdgeRight, edges.R, out var v)) { edges = new EdgeInsets(edges.T, edges.L, edges.B, v); changed = true; }

            if (changed)
            {
                Document.Edges = edges;
                UI.HandleChange(Document);
            }
        }
    }

    #endregion

    internal void ApplyEyeDropperColor(Color32 color, bool stroke)
    {
        Undo.Record(Document);
        if (stroke)
        {
            SetStrokeColor(color);
            if (ColorPicker.IsOpen(WidgetIds.StrokeColor))
                ColorPicker.Open(WidgetIds.StrokeColor, color);
        }
        else
        {
            SetFillColor(color);
            if (ColorPicker.IsOpen(WidgetIds.FillColor))
                ColorPicker.Open(WidgetIds.FillColor, color);
        }
    }

    #region Skeleton Binding

    private void CommitSkeletonBinding(SkeletonDocument skeleton)
    {
        Undo.Record(Document);
        Document.Skeleton = skeleton;
        skeleton.UpdateSprites();
    }

    private void ClearSkeletonBinding()
    {
        if (!Document.Skeleton.IsResolved)
            return;

        var skeleton = Document.Skeleton.Value;
        Undo.Record(Document);
        Document.Skeleton.Clear();
        Document.BoneName = null;
        skeleton?.UpdateSprites();
    }

    private void CommitBoneBinding(string? boneName)
    {
        Undo.Record(Document);
        Document.BoneName = boneName;
        Document.ResolveBone();
        Document.Skeleton.Value?.UpdateSprites();
    }

    #endregion

}
