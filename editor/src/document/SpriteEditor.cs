//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class SpriteEditor : DocumentEditor, IShapeEditorHost
{
    private static partial class WidgetIds
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId TileButton { get; }
        public static partial WidgetId BoneBindButton { get; }
        public static partial WidgetId BoneUnbindButton { get; }
        public static partial WidgetId SubtractButton { get; }
        public static partial WidgetId FirstOpacity { get; }
        public static partial WidgetId DopeSheet { get; }
        public static partial WidgetId FillColorButton { get; }
        public static partial WidgetId StrokeColorButton { get; }
        public static partial WidgetId LayerButton { get; }
        public static partial WidgetId BonePathButton { get; }
        public static partial WidgetId StrokeWidth { get; }
        public static partial WidgetId AddFrameButton { get; }
        public static partial WidgetId PlayButton { get; }
        public static partial WidgetId AllLayerVisibility { get; }
        public static partial WidgetId AllLayerLocked { get; }
        public static partial WidgetId HideAllLayers { get; }
        public static partial WidgetId LayerPanel { get; }
        public static partial WidgetId LayerPanelScroll { get; }
        public static partial WidgetId LayerItem { get; }
        public static partial WidgetId LayerVisibility { get; }
        public static partial WidgetId LayerLock { get; }
        public static partial WidgetId LayerSortOrder { get; }
        public static partial WidgetId AddVectorLayerBtn { get; }
        public static partial WidgetId AddLayerButton { get; }
        public static partial WidgetId RemoveLayerButton { get; }
        public static partial WidgetId ConstraintDropDown { get; }
        public static partial WidgetId SkeletonDropDown { get; }
        public static partial WidgetId ShowInSkeleton { get; }
        public static partial WidgetId ShowSkeletonOverlay { get; }
        public static partial WidgetId PathNormal { get; }
        public static partial WidgetId PathSubtract { get; }
        public static partial WidgetId PathClip { get; }
        public static partial WidgetId FillColor { get; }
        public static partial WidgetId StrokeColor { get; }
    }

    private readonly ShapeEditor _shapeEditor;
    private int _currentTimeSlot;
    private bool _isPlaying;
    private float _playTimer;
    private PopupMenuItem[] _contextMenuItems;
    private readonly int _versionOnOpen;

    public new SpriteDocument Document => (SpriteDocument)base.Document;

    public override bool ShowInspector => true;

    private int CurrentLayerFrameIndex =>
        Document.GetLayerFrameAtTimeSlot(Document.ActiveLayerIndex, _currentTimeSlot);

    private Shape CurrentShape =>
        Document.Layers[Document.ActiveLayerIndex].Frames[CurrentLayerFrameIndex].Shape;

    private SpriteLayer CurrentLayer =>
        Document.Layers[Document.ActiveLayerIndex];

    public SpriteEditor(SpriteDocument doc) : base(doc)
    {
        _versionOnOpen = doc.Version;
        _shapeEditor = new ShapeEditor(this);

        Commands =
        [
            .._shapeEditor.GetCommands(),
            new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab },
            new Command { Name = "Origin to Center", Handler = CenterShape, Key = InputCode.KeyC, Shift = true },
            new Command { Name = "Flip Horizontal", Handler = FlipHorizontal },
            new Command { Name = "Flip Vertical", Handler = FlipVertical },
            new Command { Name = "Bring Forward", Handler = MovePathUp, Key = InputCode.KeyLeftBracket },
            new Command { Name = "Send Backward", Handler = MovePathDown, Key = InputCode.KeyRightBracket },
            new Command { Name = "Toggle Playback", Handler = TogglePlayback, Key = InputCode.KeySpace },
            new Command { Name = "Previous Frame", Handler = PreviousFrame, Key = InputCode.KeyQ },
            new Command { Name = "Next Frame", Handler = NextFrame, Key = InputCode.KeyE },
            new Command { Name = "Insert Frame Before", Handler = InsertFrameBefore, Key = InputCode.KeyI },
            new Command { Name = "Insert Frame After", Handler = InsertFrameAfter, Key = InputCode.KeyO },
            new Command { Name = "Delete Frame", Handler = DeleteCurrentFrame, Key = InputCode.KeyX, Shift = true },
            new Command { Name = "Add Hold", Handler = AddHoldFrame, Key = InputCode.KeyH },
            new Command { Name = "Remove Hold", Handler = RemoveHoldFrame, Key = InputCode.KeyH, Ctrl = true },
        ];
    }

    public int CurrentTimeSlot => _currentTimeSlot;
    public bool IsPlaying => _isPlaying;

    public override void Dispose()
    {
        _shapeEditor.ClearSelection();
        // TODO: migrate to UI.PopupMenu
        EditorUI.ClosePopup();

        if (Document.Version != _versionOnOpen)
            AtlasManager.UpdateSource(Document);

        base.Dispose();
    }

    public override void OpenContextMenu(WidgetId id)
    {
        PopupMenu.Open(id, _contextMenuItems, "Sprite");
    }

    public override void OnUndoRedo()
    {
        Document.UpdateBounds();
    }

    public override void Update()
    {
        UpdateAnimation();
        _shapeEditor.HandleDeleteKey();

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(5);
            Document.DrawOrigin();
            Graphics.SetSortGroup(4);
            DrawAllLayerWireframes();
        }

        UpdateMesh();
        DrawMesh();

        if (Document.ShowSkeletonOverlay)
            DrawSkeletonOverlay();

        if (!Document.Edges.IsZero && Document.ConstrainedSize.HasValue)
            DrawEdges();
    }

    public override void LateUpdate()
    {
        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
            _shapeEditor.HandleDragStart();
        else if (Input.WasButtonReleased(InputCode.MouseLeft))
            HandleLeftClick();
    }

    private void ToolbarUI()
    {
        using var _ = UI.BeginRow(EditorStyle.SpriteEditor.Toolbar);

        using (UI.BeginRow(EditorStyle.SpriteEditor.LayerToolbar))
        {
        }

        UI.Separator(EditorStyle.Palette.PanelSeparator);
    }

    public override void UpdateUI()
    {
        using (UI.BeginColumn())
        {
            UI.Flex();

            using (UI.BeginColumn(WidgetIds.Root, EditorStyle.DocumentEditor.Root))
            {
                ToolbarUI();
                UI.Separator(EditorStyle.Palette.PanelSeparator);
                LayerDopeSheetUI();
                UI.Spacer(EditorStyle.Control.Spacing);
            }
        }
    }

    private void LayerUI(int layerIndex, SpriteLayer layer)
    {
        var isActive = Document.ActiveLayerIndex == layerIndex;
        using (UI.BeginRow(WidgetIds.LayerItem + layerIndex, isActive ? EditorStyle.SpriteEditor.LayerNameContainerActive : EditorStyle.SpriteEditor.LayerNameContainer))
        {
            var isHovered = UI.IsHovered();

            UI.Text(layer.Name, EditorStyle.Text.Primary);

            UI.Flex();

            var icon = !layer.Visible
                ? EditorAssets.Sprites.IconHidden
                : (isHovered ? EditorAssets.Sprites.IconPreview : EditorAssets.Sprites.IconEmpty);
            if (UI.Button(WidgetIds.LayerVisibility + layerIndex, icon, EditorStyle.Button.SmallIconOnly))
            {
                Undo.Record(Document);
                layer.Visible = !layer.Visible;
                Document.IncrementVersion();
            }

            UI.Spacer((EditorStyle.Control.Height - EditorStyle.SmallWidget.Height));

            icon = layer.Locked
                ? EditorAssets.Sprites.IconLock
                : (isHovered ? EditorAssets.Sprites.IconUnlock: EditorAssets.Sprites.IconEmpty);
            if (UI.Button(WidgetIds.LayerLock + layerIndex, icon, EditorStyle.Button.SmallIconOnly))
            {
                Undo.Record(Document);
                layer.Locked = !layer.Locked;
                Document.IncrementVersion();
            }

            UI.Spacer((EditorStyle.Control.Height - EditorStyle.SmallWidget.Height) * 0.5f);

            if (UI.WasPressed())
                HandleLayerClick(layer, add: Input.IsShiftDown());
        }
    }

    private void LayerDopeSheetUI()
    {
        var layers = Document.Layers;
        var maxSlots = Sprite.MaxFrames;

        using (UI.BeginColumn(new ContainerStyle { Padding = EdgeInsets.LeftRight(EditorStyle.Control.Spacing) }))
        {
            using (UI.BeginRow(EditorStyle.Dopesheet.HeaderContainer))
            {
                using (UI.BeginRow(EditorStyle.SpriteEditor.LayerNameContainer))
                {
                    if (UI.Button(WidgetIds.AddLayerButton, EditorAssets.Sprites.IconLayer, EditorStyle.Button.IconOnly))
                    {
                        Undo.Record(Document);
                        Document.AddLayer();
                    }

                    if (UI.Button(WidgetIds.RemoveLayerButton, EditorAssets.Sprites.IconDelete, EditorStyle.Button.IconOnly))
                    {
                        Undo.Record(Document);
                        Document.RemoveLayer(Document.ActiveLayerIndex);
                    }

                    UI.Flex();

                    UI.Button(WidgetIds.AllLayerVisibility, EditorAssets.Sprites.IconHidden, EditorStyle.Button.IconOnly);
                    UI.Button(WidgetIds.AllLayerLocked, EditorAssets.Sprites.IconLock, EditorStyle.Button.IconOnly);
                }

                UI.Separator(EditorStyle.Palette.PanelSeparator);
                
                var blockCount = maxSlots / 4;
                for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
                {
                    using (UI.BeginContainer(EditorStyle.Dopesheet.TimeBlock))
                    {
                        if (blockIndex > 0)
                            UI.Text(AnimationEditor.FrameTimeStrings[blockIndex], EditorStyle.Dopesheet.TimeText);
                    }
                        
                    UI.Container(EditorStyle.Dopesheet.FrameSeparator);
                }
            }

            UI.Separator(EditorStyle.Palette.PanelSeparator);

            // Layer rows (reverse order — highest layer at top)
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];
                var isSelectedLayer = Document.IsLayerActive(layer);

                using (UI.BeginRow(EditorStyle.SpriteEditor.LayerRow))
                {
                    LayerUI(i, layer);

                    UI.Container(EditorStyle.Dopesheet.FrameSeparator);

                    // Frame cells for this layer
                    var slotIndex = 0;
                    for (ushort fi = 0; fi < layer.FrameCount && slotIndex < maxSlots; fi++)
                    {
                        var isCurrentSlot = IsTimeSlotInRange(i, fi, _currentTimeSlot);

                        using (UI.BeginRow(WidgetIds.DopeSheet + i * Sprite.MaxFrames + fi))
                        {
                            if (UI.WasPressed())
                            {
                                Document.ActiveLayerIndex = i;
                                _currentTimeSlot = TimeSlotForLayerFrame(i, fi);
                            }

                            using (UI.BeginContainer(isCurrentSlot
                                ? EditorStyle.Dopesheet.SelectedFrame
                                : EditorStyle.Dopesheet.Frame))
                            {
                                UI.Container(isCurrentSlot
                                    ? EditorStyle.Dopesheet.SelectedFrameDot
                                    : EditorStyle.Dopesheet.FrameDot);
                            }

                            slotIndex++;

                            // Hold cells
                            var hold = layer.Frames[fi].Hold;
                            for (int h = 0; h < hold && slotIndex < maxSlots; h++, slotIndex++)
                            {
                                using (UI.BeginContainer(isCurrentSlot
                                    ? EditorStyle.Dopesheet.SelectedFrame
                                    : EditorStyle.Dopesheet.Frame))
                                {
                                }

                                if (h < hold - 1)
                                    UI.Container(isCurrentSlot
                                        ? EditorStyle.Dopesheet.SelectedHoldSeparator
                                        : EditorStyle.Dopesheet.HoldSeparator);
                            }

                            UI.Container(EditorStyle.Dopesheet.FrameSeparator);
                        }
                    }

                    // Empty cells after the layer's frames
                    for (; slotIndex < maxSlots; slotIndex++)
                    {
                        UI.Container(slotIndex % 4 == 0
                            ? EditorStyle.Dopesheet.FourthEmptyFrame
                            : EditorStyle.Dopesheet.EmptyFrame);
                        UI.Container(EditorStyle.Dopesheet.FrameSeparator);
                    }
                }

                UI.Container(EditorStyle.Dopesheet.LayerSeparator);
            }

        }
    }

    /// <summary>Check if a global time slot falls within a specific layer frame.</summary>
    private bool IsTimeSlotInRange(int layerIndex, int frameIndex, int timeSlot)
    {
        var layer = Document.Layers[layerIndex];
        var accumulated = 0;
        for (var f = 0; f < layer.FrameCount; f++)
        {
            var slots = 1 + layer.Frames[f].Hold;
            if (f == frameIndex)
                return timeSlot >= accumulated && timeSlot < accumulated + slots;
            accumulated += slots;
        }
        return false;
    }

    private static bool PathHasSelectedAnchor(Shape shape, Shape.Path path)
    {
        for (ushort a = 0; a < path.AnchorCount; a++)
        {
            var anchor = shape.GetAnchor((ushort)(path.AnchorStart + a));
            if (anchor.IsSelected)
                return true;
        }
        return false;
    }


    private void SetConstraint(Vector2Int? size)
    {
        Undo.Record(Document);
        Document.ConstrainedSize = size;
        Document.UpdateBounds();
        // TODO: migrate to UI.PopupMenu
        EditorUI.ClosePopup();
    }

    private static readonly ContainerStyle LayerItemSelected = new()
    {
        Color = Color.FromRgba(0x4488FF40),
        BorderRadius = 4f
    };

    private static readonly ContainerStyle LayerItemHover = new()
    {
        Color = Color.FromRgba(0xFFFFFF10),
        BorderRadius = 4f
    };

    private void SortOrderPopupUI(int layerIndex)
    {
        void Content()
        {
            var layer = Document.Layers[layerIndex];
            var sortOrders = EditorApplication.Config.SortOrders;

            // Default (0) option
            // TODO: migrate to UI.PopupMenu
            if (EditorUI.PopupItem("Default (0)", selected: layer.SortOrder == 0))
            {
                Undo.Record(Document);
                layer.SortOrder = 0;
                Document.IncrementVersion();
                // TODO: migrate to UI.PopupMenu
                EditorUI.ClosePopup();
            }

            foreach (var so in sortOrders)
            {
                // TODO: migrate to UI.PopupMenu
                if (EditorUI.PopupItem($"{so.Label} ({so.SortOrder})", selected: layer.SortOrder == so.SortOrder))
                {
                    Undo.Record(Document);
                    layer.SortOrder = so.SortOrder;
                    Document.IncrementVersion();
                    // TODO: migrate to UI.PopupMenu
                    EditorUI.ClosePopup();
                }
            }
        }

        // TODO: migrate to UI.PopupMenu
        EditorUI.Popup(WidgetIds.LayerSortOrder + layerIndex, Content);
    }

    private void BoneBindingUI()
    {
        if (!Document.Binding.IsBound) return;

        using var _ = UI.BeginContainer(ContainerStyle.Fit);

        var currentLayer = Document.ActiveLayer;
        var currentBone = currentLayer?.Bone ?? StringId.None;

        UI.SetChecked(EditorUI.IsPopupOpen(WidgetIds.BonePathButton));
        if (UI.Button(WidgetIds.BonePathButton, () =>
        {
            UI.Image(EditorAssets.Sprites.IconBone, EditorStyle.Icon.Primary);
            if (!currentBone.IsNone)
                UI.Text(currentBone.ToString(), EditorStyle.Control.Text);
            else
                UI.Text("Root", EditorStyle.Control.Text);
            UI.Spacer(EditorStyle.Control.Spacing);
        }, EditorStyle.Button.Secondary))
            // TODO: migrate to UI.PopupMenu
            EditorUI.TogglePopup(WidgetIds.BonePathButton);

        BoneBindingPopupUI();
    }

    private void BoneBindingPopupUI()
    {
        void Content()
        {
            var skeleton = Document.Binding.Skeleton;
            if (skeleton == null)
                return;

            var currentLayer = Document.ActiveLayer;
            var currentBone = currentLayer?.Bone ?? StringId.None;

            // Root option (None = root bone)
            // TODO: migrate to UI.PopupMenu
            if (EditorUI.PopupItem("Root", selected: currentBone.IsNone))
            {
                SetLayerBone(StringId.None);
                // TODO: migrate to UI.PopupMenu
                EditorUI.ClosePopup();
            }

            // List all bones from the skeleton
            for (int i = 0; i < skeleton.BoneCount; i++)
            {
                var boneName = skeleton.Bones[i].Name;
                var boneNameValue = StringId.Get(boneName);
                // TODO: migrate to UI.PopupMenu
                if (EditorUI.PopupItem(boneName, selected: currentBone == boneNameValue))
                {
                    SetLayerBone(boneNameValue);
                    // TODO: migrate to UI.PopupMenu
                    EditorUI.ClosePopup();
                }
            }
        }

        // TODO: migrate to UI.PopupMenu
        EditorUI.Popup(WidgetIds.BonePathButton, Content);
    }

    public void SetLayerBone(StringId bone)
    {
        var currentLayer = Document.ActiveLayer;
        if (currentLayer == null) return;

        Undo.Record(Document);
        currentLayer.Bone = bone;
        Document.UpdateBounds();
        Document.IncrementVersion();
    }

    public void SetCurrentTimeSlot(int timeSlot)
    {
        var maxSlots = Document.GlobalTimeSlots;
        var newSlot = Math.Clamp(timeSlot, 0, maxSlots - 1);
        if (newSlot != _currentTimeSlot)
            _currentTimeSlot = newSlot;
    }

    private void TogglePlayback()
    {
        _isPlaying = !_isPlaying;
        _playTimer = 0;
    }

    private void NextFrame()
    {
        var layer = CurrentLayer;
        if (layer.FrameCount <= 1)
            return;

        // Navigate to the next keyframe in the current layer
        var fi = CurrentLayerFrameIndex;
        fi = (fi + 1) % layer.FrameCount;

        // Compute time slot for this frame
        _currentTimeSlot = TimeSlotForLayerFrame(Document.ActiveLayerIndex, fi);
    }

    private void PreviousFrame()
    {
        var layer = CurrentLayer;
        if (layer.FrameCount <= 1)
            return;

        var fi = CurrentLayerFrameIndex;
        fi = fi == 0 ? layer.FrameCount - 1 : fi - 1;

        _currentTimeSlot = TimeSlotForLayerFrame(Document.ActiveLayerIndex, fi);
    }

    /// <summary>Compute the first time slot that corresponds to a given layer frame index.</summary>
    private int TimeSlotForLayerFrame(int layerIndex, int frameIndex)
    {
        var layer = Document.Layers[layerIndex];
        var slot = 0;
        for (var f = 0; f < frameIndex && f < layer.FrameCount; f++)
            slot += 1 + layer.Frames[f].Hold;
        return slot;
    }

    private void InsertFrameBefore()
    {
        Undo.Record(Document);
        var layer = CurrentLayer;
        var fi = CurrentLayerFrameIndex;
        var newFrame = layer.InsertFrame(fi);
        if (newFrame >= 0)
        {
            _currentTimeSlot = TimeSlotForLayerFrame(Document.ActiveLayerIndex, newFrame);
            Document.IncrementVersion();
            AtlasManager.UpdateSource(Document);
        }
    }

    private void InsertFrameAfter()
    {
        Undo.Record(Document);
        var layer = CurrentLayer;
        var fi = CurrentLayerFrameIndex;
        var newFrame = layer.InsertFrame(fi + 1);
        if (newFrame >= 0)
        {
            _currentTimeSlot = TimeSlotForLayerFrame(Document.ActiveLayerIndex, newFrame);
            Document.IncrementVersion();
            AtlasManager.UpdateSource(Document);
        }
    }

    private void DeleteCurrentFrame()
    {
        var layer = CurrentLayer;
        if (layer.FrameCount <= 1) return;
        Undo.Record(Document);
        var fi = layer.DeleteFrame(CurrentLayerFrameIndex);
        _currentTimeSlot = TimeSlotForLayerFrame(Document.ActiveLayerIndex, fi);
        Document.IncrementVersion();
        AtlasManager.UpdateSource(Document);
    }

    private void AddHoldFrame()
    {
        Undo.Record(Document);
        CurrentLayer.Frames[CurrentLayerFrameIndex].Hold++;
        Document.IncrementVersion();
    }

    private void RemoveHoldFrame()
    {
        var frame = CurrentLayer.Frames[CurrentLayerFrameIndex];
        if (frame.Hold <= 0)
            return;

        Undo.Record(Document);
        frame.Hold = Math.Max(0, frame.Hold - 1);
        Document.IncrementVersion();
    }

    private void SetFillColor(Color32 color)
    {
        Document.CurrentFillColor = color;

        EnumerateSelectedPaths((layer, shape, p) =>
        {
            shape.SetPathFillColor(p, color);
            _meshVersion = -1;
        });
    }

    private void SetStrokeColor(Color32 color)
    {
        Document.CurrentStrokeColor = color;

        EnumerateSelectedPaths((layer, shape, p) =>
        {
            shape.SetPathStroke(p, color, Document.CurrentStrokeWidth);
            _meshVersion = -1;
        });
    }

    private void SetStrokeWidth(byte width)
    {
        Document.CurrentStrokeWidth = width;

        Undo.Record(Document);

        var shape = CurrentShape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathStroke(p, path.StrokeColor, width);
        }

        Document.IncrementVersion();
    }

    private void CyclePathOperation()
    {
        // Cycle: Normal → Subtract → Clip → Normal
        Document.CurrentOperation = Document.CurrentOperation switch
        {
            PathOperation.Normal => PathOperation.Subtract,
            PathOperation.Subtract => PathOperation.Clip,
            _ => PathOperation.Normal,
        };

        var shape = CurrentShape;
        var anySelected = false;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            if (!anySelected)
            {
                Undo.Record(Document);
                anySelected = true;
            }
            shape.SetPathOperation(p, Document.CurrentOperation);
        }

        if (anySelected)
        {
            Document.IncrementVersion();
        }
    }

    private void EnumerateSelectedPaths(Action<SpriteLayer, Shape, ushort> callback)
    {
        foreach (var layer in Document.Layers)
        {
            var frameIdx = SpriteDocument.GetLayerFrameAtTimeSlot(layer, _currentTimeSlot);
            var shape = layer.Frames[frameIdx].Shape;
            for (ushort p = 0; p < shape.PathCount; p++)
            {
                ref readonly var path = ref shape.GetPath(p);
                if (!path.IsSelected) continue;
                callback(layer, shape, p);
            }
        }
    }

    private void SetPathOperation(PathOperation operation)
    {
        Undo.Record(Document);
        Document.CurrentOperation = operation;
        EnumerateSelectedPaths((layer, shape, p) =>
        {
            shape.SetPathOperation(p, operation);
        });
    }

    private void StrokeWidthButtonUI()
    {
        UI.SetChecked(EditorUI.IsPopupOpen(WidgetIds.StrokeWidth));
        if (UI.Button(WidgetIds.StrokeWidth, () =>
        {
            UI.Text($"{Document.CurrentStrokeWidth}px", EditorStyle.Control.Text);
        }, EditorStyle.Button.Secondary))
            // TODO: migrate to UI.PopupMenu
            EditorUI.TogglePopup(WidgetIds.StrokeWidth);

        StrokeWidthPopupUI();
    }

    private void StrokeWidthPopupUI()
    {
        void Content()
        {
            for (var i = 1; i <= 8; i++)
            {
                // TODO: migrate to UI.PopupMenu
                if (EditorUI.PopupItem($"{i}px", selected: Document.CurrentStrokeWidth == i))
                {
                    SetStrokeWidth((byte)i);
                    // TODO: migrate to UI.PopupMenu
                    EditorUI.ClosePopup();
                }
            }
        }

        // TODO: migrate to UI.PopupMenu
        EditorUI.Popup(WidgetIds.StrokeWidth, Content);
    }

    private void CenterShape()
    {
        Undo.Record(Document);

        // Compute world-space bounding box from all anchor positions across all layers/frames
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var hasAnchors = false;

        foreach (var layer in Document.Layers)
        {
            for (ushort fi = 0; fi < layer.FrameCount; fi++)
            {
                var shape = layer.Frames[fi].Shape;
                if (shape.AnchorCount == 0) continue;
                hasAnchors = true;
                shape.UpdateSamples();

                for (ushort a = 0; a < shape.AnchorCount; a++)
                {
                    ref readonly var anchor = ref shape.GetAnchor(a);
                    min = Vector2.Min(min, anchor.Position);
                    max = Vector2.Max(max, anchor.Position);

                    if (MathF.Abs(anchor.Curve) > 0.0001f)
                    {
                        var samples = shape.GetSegmentSamples(a);
                        for (var s = 0; s < Shape.MaxSegmentSamples; s++)
                        {
                            min = Vector2.Min(min, samples[s]);
                            max = Vector2.Max(max, samples[s]);
                        }
                    }
                }
            }
        }

        if (hasAnchors)
        {
            // Pixel-snap the center for clean alignment
            var dpi = EditorApplication.Config.PixelsPerUnit;
            var invDpi = 1f / dpi;
            var centerWorld = (min + max) * 0.5f;
            var center = new Vector2(
                MathF.Round(centerWorld.X * dpi) * invDpi,
                MathF.Round(centerWorld.Y * dpi) * invDpi);

            foreach (var layer in Document.Layers)
                for (ushort fi = 0; fi < layer.FrameCount; fi++)
                    layer.Frames[fi].Shape.SetOrigin(center);
        }

        Document.UpdateBounds();
        Document.IncrementVersion();
        Notifications.Add("origin → center");
    }

    private void FlipHorizontal()
    {
        var shape = CurrentShape;
        var pivot = shape.GetSelectedPathsCenter();
        if (!pivot.HasValue)
            return;

        Undo.Record(Document);
        shape.FlipSelectedPathsHorizontal(pivot.Value);
        shape.UpdateSamples();
        shape.UpdateBounds();
        Document.UpdateBounds();
        Document.IncrementVersion();
    }

    private void FlipVertical()
    {
        var shape = CurrentShape;
        var pivot = shape.GetSelectedPathsCenter();
        if (!pivot.HasValue)
            return;

        Undo.Record(Document);
        shape.FlipSelectedPathsVertical(pivot.Value);
        shape.UpdateSamples();
        shape.UpdateBounds();
        Document.UpdateBounds();
        Document.IncrementVersion();
    }

    private void MovePathUp()
    {
        var shape = CurrentShape;
        if (!shape.HasSelectedPaths())
            return;

        Undo.Record(Document);
        if (!shape.MoveSelectedPathUp())
            return;

        Document.IncrementVersion();
    }

    private void MovePathDown()
    {
        var shape = CurrentShape;
        if (!shape.HasSelectedPaths())
            return;

        Undo.Record(Document);
        if (!shape.MoveSelectedPathDown())
            return;

        Document.IncrementVersion();
    }

    private void UpdateAnimation()
    {
        if (!_isPlaying || Document.GlobalTimeSlots <= 1)
            return;

        _playTimer += Time.DeltaTime;
        var slotDuration = 1f / 12f;

        if (_playTimer >= slotDuration)
        {
            _playTimer = 0;
            _currentTimeSlot = (_currentTimeSlot + 1) % Document.GlobalTimeSlots;
        }
    }

    private void HandleLeftClick()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        var shift = Input.IsShiftDown(InputScope.All);
        var layers = Document.Layers;

        // First pass: check all layers for anchor/segment hits (priority over path containment)
        for (int layerIdx = layers.Count - 1; layerIdx >= 0; layerIdx--)
        {
            var layer = layers[layerIdx];
            if (!layer.Visible || layer.Locked) continue;

            var frameIdx = Document.GetLayerFrameAtTimeSlot(layerIdx, _currentTimeSlot);
            var shape = layer.Frames[frameIdx].Shape;

            Span<Shape.HitResult> hits = stackalloc Shape.HitResult[Shape.MaxAnchors];
            var hitCount = shape.HitTestAll(localMousePos, hits);

            Span<ushort> anchorHits = stackalloc ushort[hitCount];
            Span<ushort> segmentHits = stackalloc ushort[hitCount];
            var anchorCount = 0;
            var segmentCount = 0;

            for (var i = 0; i < hitCount; i++)
            {
                if (hits[i].AnchorIndex != ushort.MaxValue)
                    anchorHits[anchorCount++] = hits[i].AnchorIndex;
                else if (hits[i].SegmentIndex != ushort.MaxValue)
                    segmentHits[segmentCount++] = hits[i].SegmentIndex;
            }

            if (anchorCount == 0 && segmentCount == 0)
                continue;

            SortByPathIndexDescending(shape, anchorHits[..anchorCount]);
            SortByPathIndexDescending(shape, segmentHits[..segmentCount]);

            if (anchorCount > 0)
            {
                if (shift)
                    ShiftSelectNext(anchorHits[..anchorCount], shape.IsAnchorSelected, shape.SetAnchorSelected);
                else
                {
                    var nextIdx = FindNextInCycle(anchorHits[..anchorCount], shape.IsAnchorSelected);
                    _shapeEditor.SelectAnchor(shape, anchorHits[nextIdx], toggle: false);
                }
            }
            else
            {
                if (shift)
                    ShiftSelectNextSegment(shape, segmentHits[..segmentCount]);
                else
                {
                    var nextIdx = FindNextInCycle(segmentHits[..segmentCount], shape.IsSegmentSelected);
                    _shapeEditor.SelectSegment(shape, segmentHits[nextIdx], toggle: false);
                }
            }

            _shapeEditor.UpdateSelection();
            return;
        }

        // Second pass: check for path containment (selects layer)
        for (int layerIdx = layers.Count - 1; layerIdx >= 0; layerIdx--)
        {
            var layer = layers[layerIdx];
            if (!layer.Visible || layer.Locked) continue;

            var frameIdx = Document.GetLayerFrameAtTimeSlot(layerIdx, _currentTimeSlot);
            var shape = layer.Frames[frameIdx].Shape;

            Span<ushort> pathHits = stackalloc ushort[Shape.MaxPaths];
            var pathCount = shape.GetPathsContainingPoint(localMousePos, pathHits);

            if (pathCount == 0)
                continue;

            pathHits[..pathCount].Reverse();

            Document.ActiveLayerIndex = layerIdx;

            if (shift)
                ShiftSelectNext(pathHits[..pathCount], shape.IsPathSelected, i => shape.SetPathSelected(i, true), i => shape.SetPathSelected(i, false));
            else
            {
                var nextIdx = FindNextInCycle(pathHits[..pathCount], shape.IsPathSelected);
                _shapeEditor.SelectPath(shape, pathHits[nextIdx], toggle: false);
            }

            _shapeEditor.UpdateSelection();
            return;
        }

        // Nothing hit on any layer — clear selection
        if (!shift)
        {
            _shapeEditor.ClearSelection();
            _shapeEditor.UpdateSelection();
        }
    }

    private static int FindNextInCycle(Span<ushort> items, Func<ushort, bool> isSelected)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (isSelected(items[i]))
                return (i + 1) % items.Length;
        }
        return 0;
    }

    private static void ShiftSelectNext(Span<ushort> items, Func<ushort, bool> isSelected, Action<ushort, bool> setSelected)
    {
        // Find first unselected and select it, or deselect all if all selected
        for (var i = 0; i < items.Length; i++)
        {
            if (!isSelected(items[i]))
            {
                setSelected(items[i], true);
                return;
            }
        }
        for (var i = 0; i < items.Length; i++)
            setSelected(items[i], false);
    }

    private static void ShiftSelectNext(Span<ushort> items, Func<ushort, bool> isSelected, Action<ushort> select, Action<ushort> deselect)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (!isSelected(items[i]))
            {
                select(items[i]);
                return;
            }
        }
        for (var i = 0; i < items.Length; i++)
            deselect(items[i]);
    }

    private static void ShiftSelectNextSegment(Shape shape, Span<ushort> items)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (!shape.IsSegmentSelected(items[i]))
            {
                shape.SetAnchorSelected(items[i], true);
                shape.SetAnchorSelected(shape.GetNextAnchorIndex(items[i]), true);
                return;
            }
        }
        for (var i = 0; i < items.Length; i++)
        {
            shape.SetAnchorSelected(items[i], false);
            shape.SetAnchorSelected(shape.GetNextAnchorIndex(items[i]), false);
        }
    }

    private static void SortByPathIndexDescending(Shape shape, Span<ushort> indices)
    {
        indices.Sort((a, b) => shape.GetAnchor(b).Path.CompareTo(shape.GetAnchor(a).Path));
    }

    private void DrawAllLayerWireframes()
    {
        var layers = Document.Layers;

        // Draw non-active visible layers (dimmed segments + selected anchors only)
        for (int layerIdx = 0; layerIdx < layers.Count; layerIdx++)
        {
            if (layerIdx == Document.ActiveLayerIndex) continue;
            var layer = layers[layerIdx];
            if (!layer.Visible) continue;

            var frameIdx = Document.GetLayerFrameAtTimeSlot(layerIdx, _currentTimeSlot);
            var shape = layer.Frames[frameIdx].Shape;
            ShapeEditor.DrawSegments(shape, dimmed: true);
            ShapeEditor.DrawAnchors(shape, selectedOnly: true);
        }

        // Draw active layer on top (full brightness + all anchors)
        {
            var shape = CurrentShape;
            ShapeEditor.DrawSegments(shape, dimmed: false);
            ShapeEditor.DrawAnchors(shape);
        }
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

            // Top edge
            if (edgeT > 0)
            {
                var y = bounds.Top + edgeT;
                Gizmos.DrawLine(new Vector2(bounds.Left, y), new Vector2(bounds.Right, y), lineWidth);
            }

            // Bottom edge
            if (edgeB > 0)
            {
                var y = bounds.Bottom - edgeB;
                Gizmos.DrawLine(new Vector2(bounds.Left, y), new Vector2(bounds.Right, y), lineWidth);
            }

            // Left edge
            if (edgeL > 0)
            {
                var x = bounds.Left + edgeL;
                Gizmos.DrawLine(new Vector2(x, bounds.Top), new Vector2(x, bounds.Bottom), lineWidth);
            }

            // Right edge
            if (edgeR > 0)
            {
                var x = bounds.Right - edgeR;
                Gizmos.DrawLine(new Vector2(x, bounds.Top), new Vector2(x, bounds.Bottom), lineWidth);
            }
        }
    }

    private void DrawSkeletonOverlay()
    {
        var skeleton = Document.Binding.Skeleton;
        if (skeleton == null)
            return;

        // Skinned sprites work in skeleton space - draw other sprites at their positions
        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(0);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            foreach (var sprite in skeleton.Sprites)
            {
                if (sprite == Document) continue;
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
            {
                var boneName = StringId.Get(skeleton.Bones[boneIndex].Name);
                var currentBone = Document.ActiveLayer?.Bone ?? StringId.None;
                Gizmos.DrawBoneAndJoints(skeleton, boneIndex, selected: boneName == currentBone);
            }
        }
    }

    private void SpriteInspectorUI()
    {
        if (_shapeEditor.HasPathSelection)
            return;

        using var _ = Inspector.BeginSection("SPRITE");
        if (Inspector.IsSectionCollapsed) return;

        using (Inspector.BeginRow())
        { 
            // Constraint
            using (UI.BeginFlex())
            {
                var sizes = EditorApplication.Config.SpriteSizes;
                var constraintLabel = "None";
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
                new PopupMenuItem { Label = "None", Handler = () => SetConstraint(null)}
                ], constraintLabel, EditorAssets.Sprites.IconConstraint);
            }

        }

        // Skeleton
        using (Inspector.BeginRow())
        {
            using (UI.BeginFlex())
            {
                var skeletonLabel = Document.Binding.IsBound
                    ? StringId.Get(Document.Binding.Skeleton!.Name).ToString()
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
                    return skeletonItems.ToArray();
                }, skeletonLabel, EditorAssets.Sprites.IconBone);
            }

            if (Document.Binding.IsBound)
            {
                UI.SetChecked(Document.ShowInSkeleton);
                if (UI.Button(WidgetIds.ShowInSkeleton, EditorAssets.Sprites.IconPreview, EditorStyle.Button.ToggleIcon))
                {
                    Undo.Record(Document);
                    Document.ShowInSkeleton = !Document.ShowInSkeleton;
                    Document.Binding.Skeleton?.UpdateSprites();
                }

                UI.SetChecked(Document.ShowSkeletonOverlay);
                if (UI.Button(WidgetIds.ShowSkeletonOverlay, EditorAssets.Sprites.IconBone, EditorStyle.Button.ToggleIcon))
                {
                    Undo.Record(Document);
                    Document.ShowSkeletonOverlay = !Document.ShowSkeletonOverlay;
                }
            }
        }
    }


    private void PathInspectorUI()
    {
        if (!_shapeEditor.HasPathSelection)
            return;

        using (Inspector.BeginSection("PATH"))
        {
            if (!Inspector.IsSectionCollapsed)
            {
                using (Inspector.BeginRow())
                {
                    UI.SetChecked(Document.CurrentOperation == PathOperation.Normal);
                    if (UI.Button(WidgetIds.PathNormal, EditorAssets.Sprites.IconFill, EditorStyle.Button.ToggleIcon))
                        SetPathOperation(PathOperation.Normal);

                    UI.SetChecked(Document.CurrentOperation == PathOperation.Subtract);
                    if (UI.Button(WidgetIds.PathSubtract, EditorAssets.Sprites.IconSubtract, EditorStyle.Button.ToggleIcon))
                        SetPathOperation(PathOperation.Subtract);

                    UI.SetChecked(Document.CurrentOperation == PathOperation.Clip);
                    if (UI.Button(WidgetIds.PathClip, EditorAssets.Sprites.IconClip, EditorStyle.Button.ToggleIcon))
                        SetPathOperation(PathOperation.Clip);
                }
            }
        }

        using (Inspector.BeginSection("FILL"))
        {
            if (!Inspector.IsSectionCollapsed)
            {
                using (Inspector.BeginRow())
                {
                    using var __ = UI.BeginFlex();
                    var fillColor = Document.CurrentFillColor;
                    if (EditorUI.ColorButton(WidgetIds.FillColor, ref fillColor, EditorStyle.Inspector.ColorButton))
                    {
                        UI.HandleChange(Document);
                        SetFillColor(fillColor);
                    }
                }
            }
        }

        using (Inspector.BeginSection("STROKE"))
        {
            if (!Inspector.IsSectionCollapsed)
            {
                using (Inspector.BeginRow())
                {
                    using var __ = UI.BeginFlex();
                    var strokeColor = Document.CurrentStrokeColor;
                    if (EditorUI.ColorButton(WidgetIds.StrokeColor, ref strokeColor, EditorStyle.Inspector.ColorButton))
                    {
                        UI.HandleChange(Document);
                        SetStrokeColor(strokeColor);
                    }
                }
            }
        }
    }

    public override void InspectorUI()
    {
        SpriteInspectorUI();
        PathInspectorUI();
    }

    #region Skeleton Binding

    private void CommitSkeletonBinding(SkeletonDocument skeleton)
    {
        Undo.Record(Document);
        Document.Binding.Set(skeleton);
        skeleton.UpdateSprites();
        Notifications.Add($"bound to skeleton '{skeleton.Name}'");
    }

    private void ClearSkeletonBinding()
    {
        if (!Document.Binding.IsBound)
        {
            Notifications.Add("sprite has no skeleton binding");
            return;
        }

        var skeleton = Document.Binding.Skeleton;
        Undo.Record(Document);
        Document.Binding.Clear();
        skeleton?.UpdateSprites();
        Notifications.Add("skeleton binding cleared");
    }

    #endregion

    private void HandleLayerClick(SpriteLayer layer, bool add)
    {
        var frameIdx = SpriteDocument.GetLayerFrameAtTimeSlot(layer, _currentTimeSlot);
        var shape = layer.Frames[frameIdx].Shape;

        if (!add)
            _shapeEditor.ClearSelection();

        Document.ActiveLayerIndex = layer.Index;

        var allSelected = true;
        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            if (!shape.GetAnchor(i).IsSelected) { allSelected = false; break; }
        }

        if (shape.AnchorCount > 0 && !allSelected)
            shape.SelectAll();
        else
            shape.ClearSelection();

        _shapeEditor.UpdateSelection();
    }

    #region IShapeEditorHost

    Document IShapeEditorHost.Document => base.Document;
    Shape IShapeEditorHost.CurrentShape => CurrentShape;
    Color32 IShapeEditorHost.NewPathFillColor => Document.CurrentFillColor;
    PathOperation IShapeEditorHost.NewPathOperation => Document.CurrentOperation;
    bool IShapeEditorHost.SnapToPixelGrid => Input.IsCtrlDown(InputScope.All);

    void IShapeEditorHost.OnSelectionChanged(bool hasSelection)
    {
        foreach (var layer in Document.Layers)
        {
            var frameIdx = SpriteDocument.GetLayerFrameAtTimeSlot(layer, _currentTimeSlot);
            var shape = layer.Frames[frameIdx].Shape;

            for (ushort p = (ushort)(shape.PathCount - 1); p < shape.PathCount; p--)
            {
                ref readonly var path = ref shape.GetPath(p);
                if (!path.IsSelected) continue;

                Document.CurrentFillColor = path.FillColor;
                Document.CurrentStrokeColor = path.StrokeColor;
                Document.CurrentStrokeWidth = (byte)int.Max(1, (int)path.StrokeWidth);
                Document.CurrentOperation = path.Operation;
            }
        }
    }

    void IShapeEditorHost.ClearAllSelections()
    {
        foreach (var layer in Document.Layers)
            for (ushort fi = 0; fi < layer.FrameCount; fi++)
                layer.Frames[fi].Shape.ClearSelection();
    }

    void IShapeEditorHost.InvalidateMesh() => _meshVersion = -1;

    Shape? IShapeEditorHost.GetShapeWithSelection()
    {
        foreach (var layer in Document.Layers)
        {
            if (!layer.Visible || layer.Locked) continue;
            var frameIdx = SpriteDocument.GetLayerFrameAtTimeSlot(layer, _currentTimeSlot);
            var shape = layer.Frames[frameIdx].Shape;
            if (shape.HasSelection()) return shape;
        }
        return null;
    }

    void IShapeEditorHost.ForEachEditableShape(Action<Shape> action)
    {
        foreach (var layer in Document.Layers)
        {
            if (!layer.Visible || layer.Locked) continue;
            var frameIdx = SpriteDocument.GetLayerFrameAtTimeSlot(layer, _currentTimeSlot);
            action(layer.Frames[frameIdx].Shape);
        }
    }

    #endregion
}
