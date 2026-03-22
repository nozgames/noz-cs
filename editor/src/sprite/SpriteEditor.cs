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
    }

    private static readonly WordGenerator _wordGenerator = new();

    private int _currentTimeSlot;
    private bool _isPlaying;
    private float _playTimer;
    //private PopupMenuItem[] _contextMenuItems;
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
                new Command { Name = "Generate", Handler = () => Document.GenerateAsync(), Key = InputCode.KeyG, Ctrl = true },
                new Command { Name = "Eye Dropper", Handler = BeginEyeDropper, Key = InputCode.KeyI },
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

    public override void OpenContextMenu(WidgetId id)
    {
        //PopupMenu.Open(id, _contextMenuItems, "Sprite");
    }

    public override void OnUndoRedo()
    {
        Document.UpdateBounds();
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
        // Tool group: Pen, Knife, Rect, Circle
        var activeTool = Workspace.ActiveTool;

        UI.SetChecked(activeTool is PenTool);
        if (FloatingToolbar.Button(WidgetIds.PenToolButton, EditorAssets.Sprites.IconEdit))
            BeginPenTool();

        UI.SetChecked(activeTool is KnifeTool);
        if (FloatingToolbar.Button(WidgetIds.KnifeToolButton, EditorAssets.Sprites.IconClose))
            BeginKnifeTool();

        UI.SetChecked(activeTool is ShapeTool { ShapeType: ShapeType.Rectangle });
        if (FloatingToolbar.Button(WidgetIds.RectToolButton, EditorAssets.Sprites.IconLayer))
            BeginRectangleTool();

        UI.SetChecked(activeTool is ShapeTool { ShapeType: ShapeType.Circle });
        if (FloatingToolbar.Button(WidgetIds.CircleToolButton, EditorAssets.Sprites.IconCircle))
            BeginCircleTool();

        FloatingToolbar.Divider();

        // Add frame
        if (FloatingToolbar.Button(WidgetIds.AddFrameButton, EditorAssets.Sprites.IconKeyframe))
            InsertFrameAfter();

        FloatingToolbar.Divider();

        // Toggle group: Tile
        UI.SetChecked(Document.ShowTiling);
        if (FloatingToolbar.Button(WidgetIds.TileButton, EditorAssets.Sprites.IconTiling))
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

        var path = GetPathWithSelection();
        if (path == null) return;
        path.FillColor = color;
        _meshVersion = -1;
    }

    private void SetStrokeColor(Color32 color)
    {
        Document.CurrentStrokeColor = color;

        var path = GetPathWithSelection();
        if (path == null) return;
        path.StrokeColor = color;
        path.StrokeWidth = Document.CurrentStrokeWidth;
        _meshVersion = -1;
    }

    private void SetStrokeWidth(byte width)
    {
        Document.CurrentStrokeWidth = width;
        Undo.Record(Document);

        var path = GetPathWithSelection();
        if (path == null) return;
        path.StrokeWidth = width;
    }

    private void CycleSpritePathOperation()
    {
        Document.CurrentOperation = Document.CurrentOperation switch
        {
            SpritePathOperation.Normal => SpritePathOperation.Subtract,
            SpritePathOperation.Subtract => SpritePathOperation.Clip,
            _ => SpritePathOperation.Normal,
        };

        var path = GetPathWithSelection();
        if (path != null)
        {
            Undo.Record(Document);
            path.Operation = Document.CurrentOperation;
            Document.IncrementVersion();
        }
    }

    private void SetSpritePathOperation(SpritePathOperation operation)
    {
        Undo.Record(Document);
        Document.CurrentOperation = operation;

        var path = GetPathWithSelection();
        if (path != null)
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
            if (p.Anchors.Count == 0) return;
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
                for (var i = 0; i < p.Anchors.Count; i++)
                {
                    var a = p.Anchors[i];
                    a.Position -= center;
                    p.Anchors[i] = a;
                }
                p.MarkDirty();
            });
        }

        Document.UpdateBounds();
        Document.IncrementVersion();
    }

    private void FlipHorizontal()
    {
        var path = GetPathWithSelection();
        if (path == null) return;

        var centroid = path.GetSelectedCentroid();
        if (!centroid.HasValue) return;

        Undo.Record(Document);
        for (var i = 0; i < path.Anchors.Count; i++)
        {
            if (!path.Anchors[i].IsSelected) continue;
            var a = path.Anchors[i];
            a.Position = new Vector2(2 * centroid.Value.X - a.Position.X, a.Position.Y);
            a.Curve = -a.Curve;
            path.Anchors[i] = a;
        }
        path.MarkDirty();
        path.UpdateSamples();
        path.UpdateBounds();
        Document.UpdateBounds();
    }

    private void FlipVertical()
    {
        var path = GetPathWithSelection();
        if (path == null) return;

        var centroid = path.GetSelectedCentroid();
        if (!centroid.HasValue) return;

        Undo.Record(Document);
        for (var i = 0; i < path.Anchors.Count; i++)
        {
            if (!path.Anchors[i].IsSelected) continue;
            var a = path.Anchors[i];
            a.Position = new Vector2(a.Position.X, 2 * centroid.Value.Y - a.Position.Y);
            a.Curve = -a.Curve;
            path.Anchors[i] = a;
        }
        path.MarkDirty();
        path.UpdateSamples();
        path.UpdateBounds();
        Document.UpdateBounds();
    }

    private void MovePathUp()
    {
        var path = GetPathWithSelection();
        if (path == null) return;

        var parent = Document.RootLayer.FindParent(path);
        if (parent == null) return;

        var idx = parent.Children.IndexOf(path);
        if (idx <= 0) return;

        Undo.Record(Document);
        parent.Children.RemoveAt(idx);
        parent.Children.Insert(idx - 1, path);
        Document.IncrementVersion();
    }

    private void MovePathDown()
    {
        var path = GetPathWithSelection();
        if (path == null) return;

        var parent = Document.RootLayer.FindParent(path);
        if (parent == null) return;

        var idx = parent.Children.IndexOf(path);
        if (idx < 0 || idx >= parent.Children.Count - 1) return;

        Undo.Record(Document);
        parent.Children.RemoveAt(idx);
        parent.Children.Insert(idx + 1, path);
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

        var hit = Document.RootLayer.HitTest(localMousePos);

        if (hit.HasValue)
        {
            var h = hit.Value;

            // Switch active layer to the hit layer
            if (h.Layer != Document.ActiveLayer)
                Document.ActiveLayer = h.Layer;

            if (h.Hit.AnchorIndex >= 0)
            {
                // Anchor hit
                if (!shift) ClearAllSelections();
                h.Path.SetAnchorSelected(h.Hit.AnchorIndex, true);
                UpdateSelection();
                return;
            }

            if (h.Hit.SegmentIndex >= 0)
            {
                // Segment hit — select both anchors
                if (!shift) ClearAllSelections();
                var nextIdx = (h.Hit.SegmentIndex + 1) % h.Path.Anchors.Count;
                h.Path.SetAnchorSelected(h.Hit.SegmentIndex, true);
                h.Path.SetAnchorSelected(nextIdx, true);
                UpdateSelection();
                return;
            }

            if (h.Hit.InPath)
            {
                // Path containment — select all anchors
                if (!shift) ClearAllSelections();
                h.Path.SelectAll();
                UpdateSelection();
                return;
            }
        }

        // Nothing hit — clear selection
        if (!shift)
        {
            ClearSelection();
            UpdateSelection();
        }
    }

    private void DrawWireframe()
    {
        var transform = Document.Transform;
        Document.RootLayer.ForEachEditablePath(path =>
        {
            if (path.Anchors.Count < 2) return;
            path.UpdateSamples();
            DrawPathSegments(path, transform);
            DrawPathAnchors(path, transform);
        });
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
                UI.SetChecked(shouldExport);
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
            UI.SetChecked(Document.ShowInSkeleton);
            if (UI.Button(WidgetIds.ShowInSkeleton, EditorAssets.Sprites.IconPreview, EditorStyle.Button.ToggleIcon))
            {
                Undo.Record(Document);
                Document.ShowInSkeleton = !Document.ShowInSkeleton;
                Document.Skeleton.Value?.UpdateSprites();
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
        if (!HasPathSelection)
            return;

        using (Inspector.BeginSection("PATH"))
        {
            if (Inspector.IsSectionCollapsed)
                return;

            using (Inspector.BeginProperty("Operation"))
            using (UI.BeginRow(EditorStyle.Control.Spacing))
            {
                UI.SetChecked(Document.CurrentOperation == SpritePathOperation.Normal);
                if (UI.Button(WidgetIds.PathNormal, EditorAssets.Sprites.IconFill, EditorStyle.Button.ToggleIcon))
                    SetSpritePathOperation(SpritePathOperation.Normal);

                UI.SetChecked(Document.CurrentOperation == SpritePathOperation.Subtract);
                if (UI.Button(WidgetIds.PathSubtract, EditorAssets.Sprites.IconSubtract, EditorStyle.Button.ToggleIcon))
                    SetSpritePathOperation(SpritePathOperation.Subtract);

                UI.SetChecked(Document.CurrentOperation == SpritePathOperation.Clip);
                if (UI.Button(WidgetIds.PathClip, EditorAssets.Sprites.IconClip, EditorStyle.Button.ToggleIcon))
                    SetSpritePathOperation(SpritePathOperation.Clip);
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

    private void GenerationStyleUI()
    {

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
            UI.SetDisabled(string.IsNullOrWhiteSpace(Document.Generation!.Prompt) || Document.Generation.Config.Value == null);
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
        if (Document.Generation?.Job.Texture == null)
            return;
        Workspace.BeginTool(new EyeDropperTool(this));
    }

    internal void ApplyEyeDropperColor(Color32 color, bool shift)
    {
        if (shift)
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
