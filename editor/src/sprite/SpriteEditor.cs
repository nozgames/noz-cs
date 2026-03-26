//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using CrypticWizard.RandomWordGenerator;

namespace NoZ.Editor;

public partial class SpriteEditor : DocumentEditor
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
        public static partial WidgetId ShowInSkeleton { get; }
        public static partial WidgetId ShowSkeletonOverlay { get; }
        public static partial WidgetId PathNormal { get; }
        public static partial WidgetId PathSubtract { get; }
        public static partial WidgetId PathClip { get; }
        public static partial WidgetId BoolUnion { get; }
        public static partial WidgetId BoolSubtract { get; }
        public static partial WidgetId BoolIntersect { get; }
        public static partial WidgetId FillColor { get; }
        public static partial WidgetId GenerateButton { get; }
        public static partial WidgetId CancelButton { get; }
        public static partial WidgetId StyleDropDown { get; }
        public static partial WidgetId GenerationPrompt { get; }
        public static partial WidgetId GenerationNegativePrompt { get; }
        public static partial WidgetId Seed { get; }
        public static partial WidgetId RandomizeSeed { get; }
        public static partial WidgetId AddGenerationButton { get; }
        public static partial WidgetId RemoveGenerationButton { get; }
        public static partial WidgetId AddReference { get; }
        public static partial WidgetId ReferenceItem { get; }
        public static partial WidgetId DeleteReference { get; }
        public static partial WidgetId ExportToggle { get; }
        public static partial WidgetId PenToolButton { get; }
        public static partial WidgetId KnifeToolButton { get; }
        public static partial WidgetId RectToolButton { get; }
        public static partial WidgetId CircleToolButton { get; }
        public static partial WidgetId GenImageToggle { get; }
        public static partial WidgetId DopeSheetToggle { get; }
        public static partial WidgetId VModeButton { get; }
        public static partial WidgetId AModeButton { get; }
    }

    private static readonly WordGenerator _wordGenerator = new();

    private int _currentTimeSlot;
    private bool _isPlaying;
    private float _playTimer;
    private readonly int _versionOnOpen;

    public new SpriteDocument Document => (SpriteDocument)base.Document;

    public override bool ShowInspector => true;

    private int CurrentFrameIndex =>
        Document.GetFrameAtTimeSlot(_currentTimeSlot);

    public SpriteEditor(SpriteDocument doc) : base(doc)
    {
        _versionOnOpen = doc.Version;

        if (doc.IsMutable)
        {
            Commands =
            [
                ..GetShapeCommands(),
                new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab },
                new Command { Name = "Origin to Center", Handler = CenterShape, Key = InputCode.KeyC, Shift = true },
                new Command { Name = "Flip Horizontal", Handler = () => FlipAxis(true) },
                new Command { Name = "Flip Vertical", Handler = () => FlipAxis(false) },
                new Command { Name = "Bring Forward", Handler = () => MovePathInOrder(-1), Key = InputCode.KeyLeftBracket },
                new Command { Name = "Send Backward", Handler = () => MovePathInOrder(1), Key = InputCode.KeyRightBracket },
                new Command { Name = "Toggle Playback", Handler = TogglePlayback, Key = InputCode.KeySpace },
                new Command { Name = "Previous Frame", Handler = PreviousFrame, Key = InputCode.KeyQ },
                new Command { Name = "Next Frame", Handler = NextFrame, Key = InputCode.KeyE },
                new Command { Name = "Insert Frame Before", Handler = InsertFrameBefore, Key = InputCode.KeyI, Ctrl = true },
                new Command { Name = "Insert Frame After", Handler = InsertFrameAfter, Key = InputCode.KeyO, Ctrl = true },
                new Command { Name = "Delete Frame", Handler = DeleteCurrentFrame, Key = InputCode.KeyX, Shift = true },
                new Command { Name = "Add Hold", Handler = AddHoldFrame, Key = InputCode.KeyH },
                new Command { Name = "Remove Hold", Handler = RemoveHoldFrame, Key = InputCode.KeyH, Ctrl = true },
                new Command { Name = "Generate", Handler = () => Document.GenerateAsync(), Key = InputCode.KeyG, Ctrl = true },
                new Command { Name = "Eye Dropper", Handler = BeginEyeDropper, Key = InputCode.KeyI },
                new Command { Name = "Boolean Union", Handler = BooleanUnion, Key = InputCode.KeyU, Ctrl = true, Shift = true },
                new Command { Name = "Boolean Subtract", Handler = BooleanSubtract, Key = InputCode.KeyD, Ctrl = true, Shift = true },
                new Command { Name = "Boolean Intersect", Handler = BooleanIntersect, Key = InputCode.KeyI, Ctrl = true, Shift = true },
            ];
        }
        else
        {
            Commands =
            [
                new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab },
            ];
        }
    }

    public int CurrentTimeSlot => _currentTimeSlot;
    public bool IsPlaying => _isPlaying;

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
        Document.UpdateBounds();
        RebuildSelectedPaths();
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

        UpdateAnimation();
        HandleDeleteKey();

        var hasGen = Document.Generation is { } gen && gen.Job.Texture != null;

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(hasGen ? 6 : 5);
            Document.DrawOrigin();
            Graphics.SetSortGroup(hasGen ? 5 : 4);
            DrawWireframe();
        }

        UpdateMeshFromLayers();

        if (hasGen)
        {
            DrawGeneratedImage(sortGroup: 1, alpha: 1f);
            DrawColoredMesh(sortGroup: 2);
            DrawGeneratedImage(sortGroup: 3, alpha: 0.3f);
        }
        else
        {
            DrawMesh();
        }

        if (Document.ShowSkeletonOverlay)
            DrawSkeletonOverlay();

        if (!Document.Edges.IsZero && Document.ConstrainedSize.HasValue)
            DrawEdges();
    }

    public override void LateUpdate()
    {
        if (!Document.IsMutable) return;

        UpdateHandleCursor();

        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
            HandleDragStart();
        else if (Input.WasButtonReleased(InputCode.MouseLeft))
            HandleLeftClick();
    }

    public override void UpdateUI()
    {
        if (!Document.IsMutable) return;

        using (UI.BeginRow())
        {
            using (UI.BeginFlex())
                OutlinerUI();

            UI.FlexSplitter(WidgetIds.OutlinerSplitter, ref _outlinerSize,
                EditorStyle.Inspector.Splitter, fixedPane: 1);

            using (UI.BeginFlex()) { }
        }
    }

    public override void UpdateOverlayUI()
    {
        if (!Document.IsMutable) return;

        using (FloatingToolbar.Begin())
        {
            FloatingToolbarUI();

            if (Document.AnimFrames.Count > 1)
            {
                FloatingToolbar.Row();
                FloatingDopeSheetUI();
            }
        }
    }

    private void FloatingToolbarUI()
    {
        // V/A mode toggles
        if (FloatingToolbar.Button(WidgetIds.VModeButton, EditorAssets.Sprites.IconMove, isSelected: CurrentMode == SpriteEditMode.V))
            SetMode(SpriteEditMode.V);

        if (FloatingToolbar.Button(WidgetIds.AModeButton, EditorAssets.Sprites.IconEdit, isSelected: CurrentMode == SpriteEditMode.A))
            SetMode(SpriteEditMode.A);

        FloatingToolbar.Divider();

        // Tool group: Pen, Knife, Rect, Circle (A mode only)
        if (CurrentMode == SpriteEditMode.A)
        {
            var activeTool = Workspace.ActiveTool;

            if (FloatingToolbar.Button(WidgetIds.PenToolButton, EditorAssets.Sprites.IconEdit, isSelected: activeTool is PenTool))
                BeginPenTool();

            if (FloatingToolbar.Button(WidgetIds.KnifeToolButton, EditorAssets.Sprites.IconClose, isSelected: activeTool is KnifeTool))
                BeginKnifeTool();

            if (FloatingToolbar.Button(WidgetIds.RectToolButton, EditorAssets.Sprites.IconLayer, isSelected: activeTool is ShapeTool { ShapeType: ShapeType.Rectangle }))
                BeginRectangleTool();

            if (FloatingToolbar.Button(WidgetIds.CircleToolButton, EditorAssets.Sprites.IconCircle, isSelected: activeTool is ShapeTool { ShapeType: ShapeType.Circle }))
                BeginCircleTool();

            FloatingToolbar.Divider();
        }

        // Add frame
        if (FloatingToolbar.Button(WidgetIds.AddFrameButton, EditorAssets.Sprites.IconKeyframe))
            InsertFrameAfter();

        FloatingToolbar.Divider();

        // Toggle group: Tile
        if (FloatingToolbar.Button(WidgetIds.TileButton, EditorAssets.Sprites.IconTiling, isSelected: Document.ShowTiling))
            Document.ShowTiling = !Document.ShowTiling;
    }

    private int TotalTimeSlots() => Document.TotalTimeSlots;

    private void FloatingDopeSheetUI()
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
                            _currentTimeSlot = TimeSlotForFrame(fi);

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
        Document.UpdateBounds();
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
            _currentTimeSlot = newSlot;
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
        _currentTimeSlot = TimeSlotForFrame(fi);
    }

    private void PreviousFrame()
    {
        if (Document.AnimFrames.Count <= 1)
            return;

        var fi = CurrentFrameIndex;
        fi = fi == 0 ? Document.AnimFrames.Count - 1 : fi - 1;
        _currentTimeSlot = TimeSlotForFrame(fi);
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
            _currentTimeSlot = TimeSlotForFrame(newFrame);
        Document.UpdateBounds();
        Document.IncrementVersion();
    }

    private void InsertFrameAfter()
    {
        Undo.Record(Document);
        var fi = CurrentFrameIndex;
        var newFrame = Document.InsertFrame(fi + 1);
        if (newFrame >= 0)
            _currentTimeSlot = TimeSlotForFrame(newFrame);
        Document.UpdateBounds();
        Document.IncrementVersion();
    }

    private void DeleteCurrentFrame()
    {
        if (Document.AnimFrames.Count <= 1) return;
        Undo.Record(Document);
        var fi = Document.DeleteFrame(CurrentFrameIndex);
        _currentTimeSlot = TimeSlotForFrame(fi);
        Document.UpdateBounds();
        Document.IncrementVersion();
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

        if (_selectedPaths.Count == 0) return;
        foreach (var path in _selectedPaths)
            path.FillColor = color;
        _meshVersion = -1;
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
        _meshVersion = -1;
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
    }

    private void CenterShape()
    {
        Undo.Record(Document);

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var hasAnchors = false;

        Document.RootLayer.ForEachEditablePath(p =>
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
            Document.RootLayer.ForEachEditablePath(p =>
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

        Document.UpdateBounds();
        Document.IncrementVersion();
    }

    private void FlipAxis(bool horizontal)
    {
        if (_selectedPaths.Count == 0) return;

        // Compute centroid across all selected paths
        var sum = Vector2.Zero;
        var count = 0;
        foreach (var path in _selectedPaths)
        {
            foreach (var contour in path.Contours)
            {
                foreach (var anchor in contour.Anchors)
                {
                    sum += anchor.Position;
                    count++;
                }
            }
        }
        if (count == 0) return;
        var centroid = sum / count;

        Undo.Record(Document);
        foreach (var path in _selectedPaths)
        {
            foreach (var contour in path.Contours)
            {
                for (var i = 0; i < contour.Anchors.Count; i++)
                {
                    var a = contour.Anchors[i];
                    a.Position = horizontal
                        ? new Vector2(2 * centroid.X - a.Position.X, a.Position.Y)
                        : new Vector2(a.Position.X, 2 * centroid.Y - a.Position.Y);
                    a.Curve = -a.Curve;
                    contour.Anchors[i] = a;
                }
            }
            path.MarkDirty();
            path.UpdateSamples();
            path.UpdateBounds();
        }
        Document.UpdateBounds();
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
        var parent = firstPath.Parent ?? ActiveLayer;
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
        Document.UpdateBounds();
        Document.IncrementVersion();
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
            _currentTimeSlot = (_currentTimeSlot + 1) % Document.TotalTimeSlots;
        }
    }

    private readonly List<SpriteNode.AnchorHitResult> _anchorHitResults = new();
    private readonly List<SpritePath> _pathHitResults = new();
    private readonly List<SpritePath> _hitPaths = new();

    private void HandleLeftClick()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        var shift = Input.IsShiftDown(InputScope.All);
        var alt = Input.IsAltDown(InputScope.All);

        // Alt+click on segment in A mode: insert anchor
        if (alt && CurrentMode == SpriteEditMode.A && HandleAltClickInsert(localMousePos))
            return;

        // A mode: try anchor selection on selected paths first
        if (CurrentMode == SpriteEditMode.A && _selectedPaths.Count > 0)
        {
            if (HandleAnchorClick(localMousePos, shift))
                return;
        }

        // Path selection fallback (works in both V and A modes)
        if (HandlePathClick(localMousePos, shift))
            return;

        // Nothing hit — clear everything
        if (!shift)
            ClearSelection();
    }

    private bool HandleAltClickInsert(Vector2 localMousePos)
    {
        var hit = Document.RootLayer.HitTestSegment(localMousePos);
        if (!hit.HasValue || !hit.Value.Path.IsSelected) return false;

        Undo.Record(Document);
        var path = hit.Value.Path;
        var hitCi = hit.Value.ContourIndex;
        path.ClearAnchorSelection();
        path.SplitSegmentAtPoint(hitCi, hit.Value.SegmentIndex, hit.Value.Position);

        var newIdx = hit.Value.SegmentIndex + 1;
        if (newIdx < path.Contours[hitCi].Anchors.Count)
            path.SetAnchorSelected(hitCi, newIdx, true);

        path.UpdateSamples();
        path.UpdateBounds();
        Document.UpdateBounds();
        return true;
    }

    private bool HandleAnchorClick(Vector2 localMousePos, bool shift)
    {
        _anchorHitResults.Clear();
        Document.RootLayer.HitTestAnchor(localMousePos, _anchorHitResults, onlySelected: true);

        foreach (var h in _anchorHitResults)
        {
            if (h.Path.Contours[h.ContourIndex].Anchors[h.AnchorIndex].IsSelected && !shift)
            {
                if (CycleAnchorSelection(h))
                    return true;
            }

            if (!shift) Document.RootLayer.ClearAnchorSelections();
            h.Path.SetAnchorSelected(h.ContourIndex, h.AnchorIndex, true);
            RebuildSelectedPaths();
            return true;
        }
        return false;
    }

    private bool CycleAnchorSelection(SpriteNode.AnchorHitResult currentHit)
    {
        var foundCurrent = false;
        foreach (var h in _anchorHitResults)
        {
            if (!h.Path.IsSelected) continue;

            if (!foundCurrent)
            {
                if (h.Path == currentHit.Path && h.AnchorIndex == currentHit.AnchorIndex)
                    foundCurrent = true;
                continue;
            }

            Document.RootLayer.ClearAnchorSelections();
            h.Path.SetAnchorSelected(h.AnchorIndex, true);
            RebuildSelectedPaths();
            return true;
        }

        // Wrap around
        foreach (var h in _anchorHitResults)
        {
            if (!h.Path.IsSelected) continue;
            Document.RootLayer.ClearAnchorSelections();
            h.Path.SetAnchorSelected(h.AnchorIndex, true);
            RebuildSelectedPaths();
            return true;
        }

        return false;
    }

    private bool HandlePathClick(Vector2 localMousePos, bool shift)
    {
        // Collect all paths hit by any method: fill, anchor, or segment
        _pathHitResults.Clear();
        Document.RootLayer.HitTestPath(localMousePos, _pathHitResults);

        _anchorHitResults.Clear();
        Document.RootLayer.HitTestAnchor(localMousePos, _anchorHitResults);

        var segHit = Document.RootLayer.HitTestSegment(localMousePos);

        _hitPaths.Clear();

        void AddPath(SpritePath path)
        {
            if (_hitPaths.Contains(path)) return;
            _hitPaths.Add(path);
            if (_hitPaths.Count == 1)
            {
                var layer = path.Parent as SpriteLayer;
                if (layer != null && layer != Document.ActiveLayer)
                    Document.ActiveLayer = layer;
            }
        }

        foreach (var p in _pathHitResults)
            AddPath(p);
        foreach (var h in _anchorHitResults)
            AddPath(h.Path);
        if (segHit.HasValue)
            AddPath(segHit.Value.Path);

        if (_hitPaths.Count == 0)
            return false;

        if (!shift)
        {
            // Without shift: select topmost, or cycle if already selected
            var topmost = _hitPaths[0];
            if (topmost.IsSelected)
            {
                // Cycle: find next unselected, or wrap
                SpritePath? next = null;
                for (var i = 1; i < _hitPaths.Count; i++)
                {
                    next = _hitPaths[i];
                    break;
                }

                Document.RootLayer.ClearSelection();
                (next ?? _hitPaths[0]).SelectPath();
            }
            else
            {
                Document.RootLayer.ClearSelection();
                topmost.SelectPath();
            }
        }
        else
        {
            // Shift: add the next unselected path to selection
            SpritePath? nextUnselected = null;
            foreach (var p in _hitPaths)
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
            else if (_hitPaths.Count > 0)
            {
                // All selected — wrap: deselect topmost, it cycles visually
                _hitPaths[0].DeselectPath();
            }
        }

        RebuildSelectedPaths();
        return true;
    }

    private void DrawWireframe()
    {
        if (_selectedPaths.Count == 0) return;

        var transform = Document.Transform;

        if (CurrentMode == SpriteEditMode.A)
        {
            // A mode: draw anchors and edges for selected paths only
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
            // V mode: draw bounding box around each selected path
            DrawSelectionBounds(transform);
        }
    }

    private void UpdateHandleCursor()
    {
        _hoverPath = null;
        _hoverAnchorIndex = -1;

        if (Workspace.ActiveTool != null)
            return;

        if (_selectedPaths.Count == 0)
        {
            SetCursor(SpritePathHandle.None);
            return;
        }

        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        if (CurrentMode == SpriteEditMode.V)
        {
            SetCursor(HitTestHandles(localMousePos));
        }
        else if (CurrentMode == SpriteEditMode.A)
        {
            var hit = Document.RootLayer.HitTestAnchor(localMousePos, onlySelected: true);
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
        var lineWidth = EditorStyle.Shape.SegmentLineWidth;
        var tl = new Vector2(bounds.X, bounds.Y);
        var tr = new Vector2(bounds.Right, bounds.Y);
        var br = new Vector2(bounds.Right, bounds.Bottom);
        var bl = new Vector2(bounds.X, bounds.Bottom);
        Gizmos.DrawLine(tl, tr, lineWidth, order: 2);
        Gizmos.DrawLine(tr, br, lineWidth, order: 2);
        Gizmos.DrawLine(br, bl, lineWidth, order: 2);
        Gizmos.DrawLine(bl, tl, lineWidth, order: 2);

        // Draw handles at corners and edge midpoints (in selection space)
        var handleSize = EditorStyle.Shape.AnchorSize;
        var midX = bounds.X + bounds.Width * 0.5f;
        var midY = bounds.Y + bounds.Height * 0.5f;

        Gizmos.DrawRect(tl, handleSize, order: 6);
        Gizmos.DrawRect(tr, handleSize, order: 6);
        Gizmos.DrawRect(br, handleSize, order: 6);
        Gizmos.DrawRect(bl, handleSize, order: 6);
        Gizmos.DrawRect(new Vector2(midX, bounds.Y), handleSize, order: 6);
        Gizmos.DrawRect(new Vector2(midX, bounds.Bottom), handleSize, order: 6);
        Gizmos.DrawRect(new Vector2(bounds.X, midY), handleSize, order: 6);
        Gizmos.DrawRect(new Vector2(bounds.Right, midY), handleSize, order: 6);
    }

    private static bool IsScaleHandle(SpritePathHandle hit) => hit >= SpritePathHandle.ScaleTopLeft && hit <= SpritePathHandle.ScaleLeft;
    private static bool IsRotateHandle(SpritePathHandle hit) => hit >= SpritePathHandle.RotateTopLeft && hit <= SpritePathHandle.RotateBottomLeft;

    // Hit test handles in selection-rotated space
    private SpritePathHandle HitTestHandles(Vector2 docLocalPos)
    {
        var bounds = _selectionLocalBounds;
        if (bounds.Width <= 0 && bounds.Height <= 0) return SpritePathHandle.None;

        // Transform from document-local to selection-local space
        var selPos = Vector2.Transform(docLocalPos, Matrix3x2.CreateRotation(-_selectionRotation));

        var hitRadius = EditorStyle.Shape.AnchorHitRadius;
        var hitRadiusSqr = hitRadius * hitRadius;

        var midX = bounds.X + bounds.Width * 0.5f;
        var midY = bounds.Y + bounds.Height * 0.5f;

        // Corner positions in selection space
        Span<Vector2> corners = stackalloc Vector2[4];
        corners[0] = new Vector2(bounds.X, bounds.Y);
        corners[1] = new Vector2(bounds.Right, bounds.Y);
        corners[2] = new Vector2(bounds.Right, bounds.Bottom);
        corners[3] = new Vector2(bounds.X, bounds.Bottom);

        // Test rotation zones (outside the bounding box, same radius as scale)
        if (!bounds.Contains(selPos))
        {
            for (var i = 0; i < 4; i++)
            {
                if (Vector2.DistanceSquared(selPos, corners[i]) <= hitRadiusSqr)
                    return SpritePathHandle.RotateTopLeft + i;
            }
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
            EditorCursor.SetRotate();
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
    private Vector2 GetOppositePivotInSelSpace(SpritePathHandle hit)
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
                return skeletonItems.ToArray();
            }, skeletonLabel, EditorAssets.Sprites.IconBone);
        }

        if (Document.Skeleton.IsResolved)
        {
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
        if (!HasPathSelection)
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

    public override void InspectorUI()
    {
        SpriteInspectorUI();

        if (Document.IsMutable)
        {
            PathInspectorUI();
            GenerationInspectorUI();
        }
    }

    #region Generation

    private void GenerationInspectorUI()
    {
        if (Document.Generation == null)
        {
            static void EmptySectionContent()
            {
                ElementTree.BeginAlign(Align.Min, Align.Center);
                if (UI.Button(WidgetIds.AddGenerationButton, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
                {
                    var doc = (Workspace.ActiveDocument as SpriteDocument)!;
                    Undo.Record(doc);
                    doc.Generation = new SpriteGeneration { Prompt = " ", Seed = GenerateRandomSeed() };
                    doc.ConstrainedSize ??= new Vector2Int(256, 256);
                    doc.UpdateBounds();
                }
                ElementTree.EndAlign();
            }

            using (Inspector.BeginSection("GENERATION", content: EmptySectionContent, empty: true))
            return;
        }

        static void GenerationSectionContent()
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(WidgetIds.RemoveGenerationButton, EditorAssets.Sprites.IconDelete, EditorStyle.Inspector.SectionButton))
                ((SpriteEditor)Workspace.ActiveEditor!).RemoveGeneration();
            ElementTree.EndAlign(); 
        }

        using (Inspector.BeginSection("GENERATION", content: GenerationSectionContent, empty: Document.Generation == null))
        {
            if (Document.Generation == null || Inspector.IsSectionCollapsed) return;

            // style
            using (Inspector.BeginProperty("Style"))
                UI.DropDown(
                    WidgetIds.StyleDropDown,
                    text: Document.Generation!.Config.Name ?? "None",
                    icon: EditorAssets.Sprites.AssetIconGenstyle,
                    getItems: () =>
                    {
                        var items = new List<PopupMenuItem>
                        {
                        PopupMenuItem.Item("None", () => SetStyle(null))
                        };
                        foreach (var doc in DocumentManager.Documents)
                        {
                            if (doc is GenerationConfig styleDoc)
                                items.Add(PopupMenuItem.Item(styleDoc.Name, () => SetStyle(styleDoc)));
                        }
                        return [.. items];
                    });

            var g = Document.Generation!;

            using (Inspector.BeginProperty("Prompt"))
                g.Prompt = UI.TextInput(WidgetIds.GenerationPrompt, g.Prompt, EditorStyle.TextArea, "Prompt", Document, multiLine: true);

            using (Inspector.BeginProperty("Negative Prompt"))
                g.NegativePrompt = UI.TextInput(WidgetIds.GenerationNegativePrompt, g.NegativePrompt, EditorStyle.TextArea, "Negative Prompt", Document, multiLine: true);

            using (Inspector.BeginProperty("Seed"))
            using (UI.BeginRow())
            {
                using (UI.BeginFlex())
                {
                    g.Seed = UI.TextInput(WidgetIds.Seed, g.Seed, EditorStyle.TextInput, "Seed", Document, icon: EditorAssets.Sprites.IconSeed);
                }

                if (UI.Button(WidgetIds.RandomizeSeed, EditorAssets.Sprites.IconRandom, EditorStyle.Button.IconOnly))
                {
                    Undo.Record(Document);
                    g.Seed = GenerateRandomSeed();
                }
            }
            var job = g.Job;
            if (job.IsGenerating)
                GenerationProgressUI(job);
            else
                GenerateButtonUI(job);

        }

        GenerationReferencesUI();
    }

    private void GenerationReferencesUI()
    {
        void SectionContent()
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(WidgetIds.AddReference, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
            {
                var currentDoc = Document;
                AssetPalette.OpenSprites(
                    onPicked: doc =>
                    {
                        if (doc is SpriteDocument sprite)
                            AddReference(sprite);
                    },
                    filter: doc => doc is SpriteDocument sprite
                                   && sprite != currentDoc
                                   && !currentDoc.Generation!.References.Any(r => r.Value == sprite));
            }
            ElementTree.EndAlign();
        }

        using var _ = Inspector.BeginSection("REFERENCES", content: SectionContent, empty: Document.Generation!.References.Count == 0);

        if (Inspector.IsSectionCollapsed) return;

        for (var i = 0; i < Document.Generation!.References.Count; i++)
        {
            var refDoc = Document.Generation.References[i].Value;
            if (refDoc == null) continue;
            var itemId = WidgetIds.ReferenceItem + i;

            using (Inspector.BeginRow())
            using (UI.BeginFlex())
            {
                using (UI.BeginRow(itemId, EditorStyle.Inspector.ListItem))
                {
                    UI.Image(EditorAssets.Sprites.AssetIconSprite, EditorStyle.Control.IconSecondary);
                    using (UI.BeginFlex())
                        UI.Text(refDoc.Name, EditorStyle.Text.Primary);

                    if (UI.IsHovered(itemId))
                    {
                        if (UI.Button(WidgetIds.DeleteReference + i, EditorAssets.Sprites.IconDelete, EditorStyle.Button.IconOnly))
                            RemoveReference(i);
                    }
                }
            }
        }
    }

    private void AddReference(SpriteDocument doc)
    {
        Undo.Record(Document);
        Document.Generation!.References.Add(doc);
    }

    private void RemoveReference(int index)
    {
        Undo.Record(Document);
        Document.Generation!.References.RemoveAt(index);
    }

    private void SetStyle(GenerationConfig? style)
    {
        Undo.Record(Document);
        Document.Generation!.Config = style;
    }

    private void GenerationProgressUI(GenerationJob genImage)
    {
        using (UI.BeginColumn(new ContainerStyle
        {
            Padding = EdgeInsets.Symmetric(12, 16),
            Spacing = 10,
        }))
        {
            var progressText = genImage.GenerationState switch
            {
                GenerationState.Queued when genImage.QueuePosition > 0 =>
                    $"Queued (position {genImage.QueuePosition})",
                GenerationState.Queued => "Queued...",
                GenerationState.Running => $"Generating {(int)(genImage.GenerationProgress * 100)}%",
                _ => "Starting..."
            };

            using (UI.BeginRow(new ContainerStyle { Spacing = 8 }))
            {
                UI.Text(progressText, EditorStyle.Text.Primary with { FontSize = EditorStyle.Control.TextSize });
                UI.Flex();
                if (UI.Button(WidgetIds.CancelButton, EditorAssets.Sprites.IconClose, EditorStyle.Button.IconOnly))
                    genImage.CancelGeneration();
            }

            using (UI.BeginContainer(new ContainerStyle
            {
                Width = Size.Percent(1),
                Height = 4f,
                Background = EditorStyle.Palette.Active,
                BorderRadius = 2f
            }))
            {
                UI.Container(new ContainerStyle
                {
                    Width = Size.Percent(genImage.GenerationProgress),
                    Height = 4f,
                    Background = EditorStyle.Palette.Primary,
                    BorderRadius = 2f
                });
            }
        }
    }

    private void GenerateButtonUI(GenerationJob genImage)
    {
        if (genImage.GenerationError != null)
            UI.Text(genImage.GenerationError, EditorStyle.Text.Secondary with { Color = EditorStyle.ErrorColor });

        using (UI.BeginContainer(new ContainerStyle
        {
            Padding = EdgeInsets.Symmetric(12, 16),
        }))
        {
            using (UI.BeginEnabled(!string.IsNullOrWhiteSpace(Document.Generation!.Prompt) && Document.Generation.Config.Value != null))
                if (UI.Button(WidgetIds.GenerateButton, "Generate", EditorAssets.Sprites.IconAi, EditorStyle.Button.Primary with { Width = Size.Percent(1) }))
                    Document.GenerateAsync();
        }
    }

    private void RemoveGeneration()
    {
        Undo.Record(Document);
        Document.Generation?.Dispose();
        Document.Generation = null;
    }

    private static string GenerateRandomSeed()
    {
        var adj = _wordGenerator.GetWord(WordGenerator.PartOfSpeech.adj);
        var noun = _wordGenerator.GetWord(WordGenerator.PartOfSpeech.noun);
        return $"{adj}-{noun}";
    }

    private void BeginEyeDropper()
    {
        Workspace.BeginTool(new EyeDropperTool(this));
    }

    internal void ApplyEyeDropperColor(SpritePath path, bool alt)
    {
        Undo.Record(Document);

        if (alt)
        {
            Document.CurrentStrokeWidth = path.StrokeWidth;
            Document.CurrentStrokeJoin = path.StrokeJoin;
            SetStrokeColor(path.StrokeColor);
            if (ColorPicker.IsOpen(WidgetIds.StrokeColor))
                ColorPicker.Open(WidgetIds.StrokeColor, path.StrokeColor);
        }
        else
        {
            SetFillColor(path.FillColor);
            if (ColorPicker.IsOpen(WidgetIds.FillColor))
                ColorPicker.Open(WidgetIds.FillColor, path.FillColor);
        }
    }

    #endregion

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
        skeleton?.UpdateSprites();
    }

    #endregion

}
