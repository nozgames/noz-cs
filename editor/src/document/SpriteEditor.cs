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
        public static partial WidgetId SubtractButton { get; }
        public static partial WidgetId FirstOpacity { get; }
        public static partial WidgetId DopeSheet { get; }
        public static partial WidgetId FillColorButton { get; }
        public static partial WidgetId StrokeColor { get; }
        public static partial WidgetId StrokeWidth { get; }
        public static partial WidgetId AddFrameButton { get; }
        public static partial WidgetId PlayButton { get; }
        public static partial WidgetId ConstraintDropDown { get; }
        public static partial WidgetId SkeletonDropDown { get; }
        public static partial WidgetId ShowInSkeleton { get; }
        public static partial WidgetId ShowSkeletonOverlay { get; }
        public static partial WidgetId PathNormal { get; }
        public static partial WidgetId PathSubtract { get; }
        public static partial WidgetId PathClip { get; }
        public static partial WidgetId FillColor { get; }
    }

    private readonly ShapeEditor _shapeEditor;
    private int _currentTimeSlot;
    private bool _isPlaying;
    private float _playTimer;
    private PopupMenuItem[] _contextMenuItems;
    private readonly int _versionOnOpen;

    public new SpriteDocument Document => (SpriteDocument)base.Document;

    public override bool ShowInspector => true;

    private int CurrentFrameIndex =>
        Document.GetFrameAtTimeSlot(_currentTimeSlot);

    private Shape CurrentShape =>
        Document.Frames[CurrentFrameIndex].Shape;

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
            DrawWireframe();
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
                DopeSheetUI();
                UI.Spacer(EditorStyle.Control.Spacing);
            }
        }
    }

    private void DopeSheetUI()
    {
        var maxSlots = Sprite.MaxFrames;

        using (UI.BeginColumn(new ContainerStyle { Padding = EdgeInsets.LeftRight(EditorStyle.Control.Spacing) }))
        {
            using (UI.BeginRow(EditorStyle.Dopesheet.HeaderContainer))
            {
                using (UI.BeginRow(EditorStyle.SpriteEditor.LayerNameContainer))
                {
                    UI.Flex();
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

            // Single row of frame cells
            using (UI.BeginRow(EditorStyle.SpriteEditor.LayerRow))
            {
                using (UI.BeginRow(EditorStyle.SpriteEditor.LayerNameContainer))
                {
                    UI.Text("Frames", EditorStyle.Text.Primary);
                    UI.Flex();
                }

                UI.Container(EditorStyle.Dopesheet.FrameSeparator);

                var slotIndex = 0;
                for (ushort fi = 0; fi < Document.FrameCount && slotIndex < maxSlots; fi++)
                {
                    var isCurrentSlot = IsTimeSlotInRange(fi, _currentTimeSlot);

                    using (UI.BeginRow(WidgetIds.DopeSheet + fi))
                    {
                        if (UI.WasPressed())
                            _currentTimeSlot = TimeSlotForFrame(fi);

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
                        var hold = Document.Frames[fi].Hold;
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

                // Empty cells after frames
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

    private bool IsTimeSlotInRange(int frameIndex, int timeSlot)
    {
        var accumulated = 0;
        for (var f = 0; f < Document.FrameCount; f++)
        {
            var slots = 1 + Document.Frames[f].Hold;
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
        EditorUI.ClosePopup();
    }

    private void StrokeWidthButtonUI()
    {
        if (UI.Button(WidgetIds.StrokeWidth, text: Strings.Number(Document.CurrentStrokeWidth), icon: EditorAssets.Sprites.IconStrokeSize, EditorStyle.Button.Secondary))
            EditorUI.TogglePopup(WidgetIds.StrokeWidth);

        StrokeWidthPopupUI();
    }

    private void StrokeWidthPopupUI()
    {
        void Content()
        {
            for (var i = 1; i <= 8; i++)
            {
                if (EditorUI.PopupItem($"{i}px", selected: Document.CurrentStrokeWidth == i))
                {
                    SetStrokeWidth((byte)i);
                    EditorUI.ClosePopup();
                }
            }
        }

        EditorUI.Popup(WidgetIds.StrokeWidth, Content);
    }

    public void SetCurrentTimeSlot(int timeSlot)
    {
        var maxSlots = Document.TotalTimeSlots;
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
        if (Document.FrameCount <= 1)
            return;

        var fi = CurrentFrameIndex;
        fi = (fi + 1) % Document.FrameCount;
        _currentTimeSlot = TimeSlotForFrame(fi);
    }

    private void PreviousFrame()
    {
        if (Document.FrameCount <= 1)
            return;

        var fi = CurrentFrameIndex;
        fi = fi == 0 ? Document.FrameCount - 1 : fi - 1;
        _currentTimeSlot = TimeSlotForFrame(fi);
    }

    private int TimeSlotForFrame(int frameIndex)
    {
        var slot = 0;
        for (var f = 0; f < frameIndex && f < Document.FrameCount; f++)
            slot += 1 + Document.Frames[f].Hold;
        return slot;
    }

    private void InsertFrameBefore()
    {
        Undo.Record(Document);
        var fi = CurrentFrameIndex;
        var newFrame = Document.InsertFrame(fi);
        if (newFrame >= 0)
        {
            _currentTimeSlot = TimeSlotForFrame(newFrame);
            Document.IncrementVersion();
            AtlasManager.UpdateSource(Document);
        }
    }

    private void InsertFrameAfter()
    {
        Undo.Record(Document);
        var fi = CurrentFrameIndex;
        var newFrame = Document.InsertFrame(fi + 1);
        if (newFrame >= 0)
        {
            _currentTimeSlot = TimeSlotForFrame(newFrame);
            Document.IncrementVersion();
            AtlasManager.UpdateSource(Document);
        }
    }

    private void DeleteCurrentFrame()
    {
        if (Document.FrameCount <= 1) return;
        Undo.Record(Document);
        var fi = Document.DeleteFrame(CurrentFrameIndex);
        _currentTimeSlot = TimeSlotForFrame(fi);
        Document.IncrementVersion();
        AtlasManager.UpdateSource(Document);
    }

    private void AddHoldFrame()
    {
        Undo.Record(Document);
        Document.Frames[CurrentFrameIndex].Hold++;
        Document.IncrementVersion();
    }

    private void RemoveHoldFrame()
    {
        var frame = Document.Frames[CurrentFrameIndex];
        if (frame.Hold <= 0)
            return;

        Undo.Record(Document);
        frame.Hold = Math.Max(0, frame.Hold - 1);
        Document.IncrementVersion();
    }

    private void SetFillColor(Color32 color)
    {
        Document.CurrentFillColor = color;

        var shape = CurrentShape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathFillColor(p, color);
            _meshVersion = -1;
        }
    }

    private void SetStrokeColor(Color32 color)
    {
        Document.CurrentStrokeColor = color;

        var shape = CurrentShape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathStroke(p, color, Document.CurrentStrokeWidth);
            _meshVersion = -1;
        }
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

    private void SetPathOperation(PathOperation operation)
    {
        Undo.Record(Document);
        Document.CurrentOperation = operation;
        var shape = CurrentShape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathOperation(p, operation);
        }
    }

    private void CenterShape()
    {
        Undo.Record(Document);

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var hasAnchors = false;

        for (ushort fi = 0; fi < Document.FrameCount; fi++)
        {
            var shape = Document.Frames[fi].Shape;
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

        if (hasAnchors)
        {
            var dpi = EditorApplication.Config.PixelsPerUnit;
            var invDpi = 1f / dpi;
            var centerWorld = (min + max) * 0.5f;
            var center = new Vector2(
                MathF.Round(centerWorld.X * dpi) * invDpi,
                MathF.Round(centerWorld.Y * dpi) * invDpi);

            for (ushort fi = 0; fi < Document.FrameCount; fi++)
                Document.Frames[fi].Shape.SetOrigin(center);
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
        if (!_isPlaying || Document.TotalTimeSlots <= 1)
            return;

        _playTimer += Time.DeltaTime;
        var slotDuration = 1f / 12f;

        if (_playTimer >= slotDuration)
        {
            _playTimer = 0;
            _currentTimeSlot = (_currentTimeSlot + 1) % Document.TotalTimeSlots;
        }
    }

    private void HandleLeftClick()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        var shift = Input.IsShiftDown(InputScope.All);

        var shape = CurrentShape;

        // Check for anchor/segment hits
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

        if (anchorCount > 0 || segmentCount > 0)
        {
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

        // Check for path containment
        Span<ushort> pathHits = stackalloc ushort[Shape.MaxPaths];
        var pathCount = shape.GetPathsContainingPoint(localMousePos, pathHits);

        if (pathCount > 0)
        {
            pathHits[..pathCount].Reverse();

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

        // Nothing hit — clear selection
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

    private void DrawWireframe()
    {
        var shape = CurrentShape;
        ShapeEditor.DrawSegments(shape, dimmed: false);
        ShapeEditor.DrawAnchors(shape);
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
        var skeleton = Document.Binding.Skeleton;
        if (skeleton == null)
            return;

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
                Gizmos.DrawBoneAndJoints(skeleton, boneIndex, selected: false);
        }
    }

    private void SpriteInspectorUI()
    {
        using var _ = Inspector.BeginSection("SPRITE");
        if (Inspector.IsSectionCollapsed) return;

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

        // Skeleton
        using (Inspector.BeginProperty("Skeleton"))
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

    private void PathInspectorUI()
    {
        if (!_shapeEditor.HasPathSelection)
            return;

        using (Inspector.BeginSection("PATH"))
        {
            if (Inspector.IsSectionCollapsed)
                return;

            using (Inspector.BeginProperty("Operation"))
            using (UI.BeginRow(EditorStyle.Control.Spacing))
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

            using (Inspector.BeginProperty("Fill"))
            {
                var fillColor = Document.CurrentFillColor;
                if (EditorUI.ColorButton(WidgetIds.FillColor, ref fillColor))
                {
                    UI.HandleChange(Document);
                    SetFillColor(fillColor);
                }
            }

            using (Inspector.BeginProperty("Stroke"))
            using (UI.BeginRow(EditorStyle.Control.Spacing))
            {
                var strokeColor = Document.CurrentStrokeColor;
                if (EditorUI.ColorButton(WidgetIds.StrokeColor, ref strokeColor))
                {
                    UI.HandleChange(Document);
                    SetStrokeColor(strokeColor);
                }

                if (strokeColor.A > 0)
                    StrokeWidthButtonUI();
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

    #region IShapeEditorHost

    Document IShapeEditorHost.Document => base.Document;
    Shape IShapeEditorHost.CurrentShape => CurrentShape;
    Color32 IShapeEditorHost.NewPathFillColor => Document.CurrentFillColor;
    PathOperation IShapeEditorHost.NewPathOperation => Document.CurrentOperation;
    bool IShapeEditorHost.SnapToPixelGrid => Input.IsCtrlDown(InputScope.All);

    void IShapeEditorHost.OnSelectionChanged(bool hasSelection)
    {
        var shape = CurrentShape;
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

    void IShapeEditorHost.ClearAllSelections()
    {
        for (ushort fi = 0; fi < Document.FrameCount; fi++)
            Document.Frames[fi].Shape.ClearSelection();
    }

    void IShapeEditorHost.InvalidateMesh() => _meshVersion = -1;

    Shape? IShapeEditorHost.GetShapeWithSelection()
    {
        var shape = CurrentShape;
        if (shape.HasSelection()) return shape;
        return null;
    }

    void IShapeEditorHost.ForEachEditableShape(Action<Shape> action)
    {
        action(CurrentShape);
    }

    #endregion
}
