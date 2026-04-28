//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal enum AnimationEditorState
{
    Default,
    Play
}

internal partial class AnimationEditor : DocumentEditor
{
    internal static readonly string[] FrameTimeStrings = ["0", "4", "8", "12", "16", "20", "24", "28", "32", "36", "40", "44", "48", "52", "56", "60"];

    private static partial class WidgetIds
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId OnionSkinButton { get; }
        public static partial WidgetId PlayButton { get; }
        public static partial WidgetId LoopButton { get; }
        public static partial WidgetId AddFrameButton { get; }
        public static partial WidgetId SkeletonButton { get; }
        public static partial WidgetId ShowSkeletonButton { get; }
        public static partial WidgetId MirrorButton { get; }
        public static partial WidgetId FirstFrame { get; }        
    }

    private AnimationEditorState _state = AnimationEditorState.Default;
    private bool _showSkeleton = true;
    //private PopupMenuItem[] _contextMenuItems;
    internal Vector2 SelectionCenter => _selectionCenter;
    private Vector2 _selectionCenter;
    internal Vector2 SelectionCenterWorld => _selectionCenterWorld;
    private Vector2 _selectionCenterWorld;
    private bool _onionSkin;
    private float _playSpeed = 1f;

    public new AnimationDocument Document => (AnimationDocument)base.Document;
    public override bool ShowInspector => true;
    public override PowerMode PowerMode => _state == AnimationEditorState.Play ? PowerMode.Performance : PowerMode.Balanced;

    public AnimationEditor(AnimationDocument document) : base(document)
    {
        var exitEditCommand = new Command("Exit Edit Mode", Workspace.EndEdit, [InputCode.KeyTab]);
        var copyCommand     = new Command("Copy",           CopyKeys,          [new KeyBinding(InputCode.KeyC, ctrl:true)]);
        var pasteCommand    = new Command("Paste",          PasteKeys,         [new KeyBinding(InputCode.KeyV, ctrl:true)]);

        Commands =
        [
            exitEditCommand,
            copyCommand,
            pasteCommand,
            new Command("Toggle Playback",      TogglePlayback,         [new KeyBinding(InputCode.KeySpace)]),
            new Command("Previous Frame",       PreviousFrame,          [InputCode.KeyQ]),
            new Command("Next Frame",           NextFrame,              [InputCode.KeyE]),
            new Command("Reset Position",       ResetPosition,          [new KeyBinding(InputCode.KeyG, alt:true)]),
            new Command("Reset Rotation",       ResetRotation,          [new KeyBinding(InputCode.KeyR, alt:true)]),
            new Command("Select All",           SelectAll,              [new KeyBinding(InputCode.KeyA, ctrl:true)]),
            new Command("Insert Frame Before",  InsertFrameBefore,      [InputCode.KeyI]),
            new Command("Insert Frame After",   InsertFrameAfter,       [InputCode.KeyO]),
            new Command("Insert Frame Lerp",    InsertFrameAfterLerp,   [new KeyBinding(InputCode.KeyO, alt:true)]),
            new Command("Delete Frame",         DeleteFrame,            [InputCode.KeyX, InputCode.KeyDelete]),
            new Command("Add Hold",             AddHoldFrame,           [InputCode.KeyH]),
            new Command("Remove Hold",          RemoveHoldFrame,        [new KeyBinding(InputCode.KeyH, ctrl:true)]),
            new Command("Toggle Onion Skin",    ToggleOnionSkin,        [new KeyBinding(InputCode.KeyO, shift:true)]),
            new Command("Toggle Loop",          ToggleLoop,             [InputCode.KeyL]),
            new Command("Increase Play Speed",  IncreasePlaySpeed,      [InputCode.KeyRight]),
            new Command("Decrease Play Speed",  DecreasePlaySpeed,      [InputCode.KeyLeft]),
            new Command("Mirror Pose",          MirrorPose,             [InputCode.KeyM]),
        ];

        //bool HasSelection() => Document.SelectedBoneCount > 0;

        //_contextMenuItems = [
            //PopupMenuItem.FromCommand(copyCommand, enabled: HasSelection),
            //PopupMenuItem.FromCommand(pasteCommand, enabled: Clipboard.Is<AnimationFrameData>),
            //PopupMenuItem.Separator(),
            //PopupMenuItem.FromCommand(exitEditCommand),
        //];

        ClearSelection();
        Document.UpdateTransforms();
        SetMode(new KeyframeMode());
    }

    public override void Update()
    {
        Document.UpdateBounds();

        if (_state == AnimationEditorState.Play)
            UpdatePlayState();

        DrawEditor();
        Mode?.Draw();
    }

    public override void LateUpdate()
    {
        if (_state == AnimationEditorState.Default)
            Mode?.Update();
    }

    private void SkeletonButtonUI()
    {
        var skeleton = EditorUI.SkeletonField(WidgetIds.SkeletonButton, Document.Skeleton);
        if (UI.WasChanged())
        {
            Undo.Record(Document);
            Document.SetSkeleton(skeleton);
        }
    }

    public override void UpdateUI()
    {
    }

    public override void UpdateOverlayUI()
    {
        using (FloatingToolbar.Begin())
        {
            FloatingToolbarUI();
            FloatingToolbar.Row();
            FloatingDopeSheetUI();
        }
    }

    public override void InspectorUI()
    {
        using (Inspector.BeginSection("Animation"))
        {
            using (Inspector.BeginProperty("Skeleton"))
                SkeletonButtonUI();
        }
    }

    private void FloatingToolbarUI()
    {
        if (FloatingToolbar.Button(WidgetIds.AddFrameButton, EditorAssets.Sprites.IconKeyframe))
            InsertFrameAfter();
        if (FloatingToolbar.Button(WidgetIds.MirrorButton, EditorAssets.Sprites.IconMirror))
            MirrorPose();

        FloatingToolbar.Divider();

        if (FloatingToolbar.Button(WidgetIds.PlayButton, EditorAssets.Sprites.IconPlay, isSelected: Document.IsPlaying))
            TogglePlayback();

        FloatingToolbar.Divider();

        if (FloatingToolbar.Button(WidgetIds.ShowSkeletonButton, EditorAssets.Sprites.IconPreview, isSelected: _showSkeleton))
            _showSkeleton = !_showSkeleton;

        if (FloatingToolbar.Button(WidgetIds.LoopButton, EditorAssets.Sprites.IconLoop, isSelected: Document.IsLooping))
        {
            Undo.Record(Document);
            Document.IsLooping = !Document.IsLooping;
        }

        if (FloatingToolbar.Button(WidgetIds.OnionSkinButton, EditorAssets.Sprites.IconOnion, isSelected: _onionSkin))
            _onionSkin = !_onionSkin;
    }

    private void FloatingDopeSheetUI()
    {
        var maxSlots = AnimationDocument.MaxFrames;
        var usedSlots = 0;
        for (var i = 0; i < Document.FrameCount; i++)
            usedSlots += 1 + Document.Frames[i].Hold;

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
                        UI.Text(FrameTimeStrings[blockIndex], EditorStyle.Dopesheet.TimeText);
                }

                UI.Flex();
            }

            UI.Spacer(1);

            using (UI.BeginRow(EditorStyle.Dopesheet.FloatingLayerRow))
            {
                var slotIndex = 0;
                for (var frameIndex = 0; frameIndex < Document.FrameCount && slotIndex < maxSlots; frameIndex++)
                {
                    var selected = Document.CurrentFrame == frameIndex;

                    using (UI.BeginRow(WidgetIds.FirstFrame + frameIndex))
                    {
                        if (UI.WasPressed())
                        {
                            Document.CurrentFrame = frameIndex;
                            Document.UpdateTransforms();
                            SetDefaultState();
                        }

                        using (UI.BeginContainer(selected
                            ? EditorStyle.Dopesheet.FloatingSelectedFrame
                            : EditorStyle.Dopesheet.FloatingFrame))
                        {
                            UI.Container(selected
                                ? EditorStyle.Dopesheet.FloatingSelectedFrameDot
                                : EditorStyle.Dopesheet.FloatingFrameDot);
                        }

                        slotIndex++;

                        var hold = Document.Frames[frameIndex].Hold;
                        for (int h = 0; h < hold && slotIndex < maxSlots; h++, slotIndex++)
                        {
                            if (h < hold - 1)
                                UI.Container(selected
                                    ? EditorStyle.Dopesheet.FloatingSelectedHoldSeparator
                                    : EditorStyle.Dopesheet.FloatingHoldSeparator);

                            using (UI.BeginContainer(selected
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


    private void UpdatePlayState()
    {
        Document.UpdatePlayback(Time.DeltaTime * _playSpeed);

        if (!Document.IsPlaying)
        {
            Document.Play();
        }
    }

    internal void HandleBoxSelect(Rect bounds)
    {
        if (!Input.IsShiftDown())
            ClearSelection();

        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        var baseTransform = GetBaseTransform();
        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            var bone = skeleton.Bones[boneIndex];
            var boneTransform = Document.LocalToWorld[boneIndex] * baseTransform;
            var boneStart = Vector2.Transform(Vector2.Zero, boneTransform);
            var boneEnd = Vector2.Transform(new Vector2(bone.Length, 0), boneTransform);

            if (bounds.Contains(boneStart) || bounds.Contains(boneEnd))
                SetBoneSelected(boneIndex, true);
        }
    }

    internal Matrix3x2 GetBaseTransform()
    {
        return Matrix3x2.CreateTranslation(Document.Position);
    }

    internal bool TrySelectBone()
    {
        Span<int> bones = stackalloc int[Skeleton.MaxBones];
        var hitCount = Document.HitTestBones(GetBaseTransform(), Workspace.MouseWorldPosition, bones);
        if (hitCount == 0)
        {
            if (!Input.IsShiftDown())
                ClearSelection();
            return false;
        }

        var hitIndex = hitCount - 1;
        for (; hitIndex >= 0; hitIndex--)
        {
            if (IsBoneSelected(bones[hitIndex]))
            {
                hitIndex++;
                break;
            }
        }

        if (hitIndex < 0 || hitIndex >= hitCount)
            hitIndex = 0;

        if (!Input.IsShiftDown())
            ClearSelection();

        SetBoneSelected(bones[hitIndex], true);
        return true;
    }

    internal bool IsBoneSelected(int boneIndex) => Document.Bones[boneIndex].IsSelected;

    private bool IsAncestorSelected(int boneIndex)
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return false;

        var parentIndex = skeleton.Bones[boneIndex].ParentIndex;
        while (parentIndex >= 0)
        {
            if (IsBoneSelected(parentIndex))
                return true;
            parentIndex = skeleton.Bones[parentIndex].ParentIndex;
        }
        return false;
    }

    internal void SetBoneSelected(int boneIndex, bool selected)
    {
        if (IsBoneSelected(boneIndex) == selected)
            return;

        Document.Bones[boneIndex].IsSelected = selected;
        Document.SelectedBoneCount += selected ? 1 : -1;
    }

    internal void ClearSelection()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            SetBoneSelected(boneIndex, false);
    }

    internal void UpdateSelectionCenter()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        var center = Vector2.Zero;
        var centerCount = 0f;
        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;
            center += Vector2.Transform(Vector2.Zero, Document.LocalToWorld[boneIndex]);
            centerCount += 1f;
        }

        _selectionCenter = centerCount < 0.0001f ? center : center / centerCount;
        _selectionCenterWorld = Vector2.Transform(_selectionCenter, GetBaseTransform());
    }

    internal void SaveState()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            Document.Bones[boneIndex].SavedTransform = Document.GetFrameTransform(boneIndex, Document.CurrentFrame);

        UpdateSelectionCenter();
    }

    internal void RevertToSavedState()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            Document.GetFrameTransform(boneIndex, Document.CurrentFrame) = Document.Bones[boneIndex].SavedTransform;

        Document.UpdateTransforms();
        UpdateSelectionCenter();
    }

    private void SetDefaultState()
    {
        if (_state == AnimationEditorState.Default)
            return;

        Document.Stop();
        Document.UpdateTransforms();
        _state = AnimationEditorState.Default;
    }

    internal void ApplyMoveDelta(Vector2 delta)
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex) || IsAncestorSelected(boneIndex))
                continue;

            ref var frame = ref Document.GetFrameTransform(boneIndex, Document.CurrentFrame);
            var bone = Document.Bones[boneIndex];
            var parentIndex = skeleton.Bones[boneIndex].ParentIndex;

            if (parentIndex == -1)
            {
                frame.Position = bone.SavedTransform.Position + new Vector2(delta.X, 0);
            }
            else
            {
                Matrix3x2.Invert(Document.LocalToWorld[parentIndex], out var invParent);
                var rotatedDelta = Vector2.TransformNormal(delta, invParent);
                frame.Position = bone.SavedTransform.Position + rotatedDelta;
            }

            if (Input.IsCtrlDown())
                frame.Position = Grid.SnapToGrid(frame.Position);

            if (parentIndex == -1)
                frame.Position.Y = 0.0f;
        }

        Document.UpdateTransforms();
    }

    internal void ApplyRotateDelta(float angle)
    {
        if (MathF.Abs(angle) < 0.0001f)
            return;

        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex) || IsAncestorSelected(boneIndex))
                continue;

            ref var frame = ref Document.GetFrameTransform(boneIndex, Document.CurrentFrame);
            frame.Rotation = Document.Bones[boneIndex].SavedTransform.Rotation + angle;
        }

        Document.UpdateTransforms();
    }

    private void ResetRotation()
    {
        if (_state != AnimationEditorState.Default || Document.SelectedBoneCount <= 0)
            return;

        Undo.Record(Document);
        var skeleton = Document.Skeleton;
        if (skeleton == null) return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;
            Document.GetFrameTransform(boneIndex, Document.CurrentFrame).Rotation = 0;
        }

        Document.UpdateTransforms();
        Document.IncrementVersion();
    }

    private void ResetPosition()
    {
        if (_state != AnimationEditorState.Default || Document.SelectedBoneCount <= 0)
            return;

        Undo.Record(Document);
        var skeleton = Document.Skeleton;
        if (skeleton == null) return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;
            Document.GetFrameTransform(boneIndex, Document.CurrentFrame).Position = Vector2.Zero;
        }

        Document.UpdateTransforms();
        Document.IncrementVersion();
    }

    private void SelectAll()
    {
        if (_state != AnimationEditorState.Default)
            return;

        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            SetBoneSelected(boneIndex, true);
    }

    private void PreviousFrame()
    {
        SetDefaultState();
        Document.CurrentFrame = (Document.CurrentFrame - 1 + Document.FrameCount) % Document.FrameCount;
        Document.UpdateTransforms();
    }

    private void NextFrame()
    {
        SetDefaultState();
        Document.CurrentFrame = (Document.CurrentFrame + 1) % Document.FrameCount;
        Document.UpdateTransforms();
    }

    private void TogglePlayback()
    {
        if (_state == AnimationEditorState.Play)
        {
            SetDefaultState();
            return;
        }

        if (_state != AnimationEditorState.Default)
            return;

        Document.Play();
        _state = AnimationEditorState.Play;
    }

    private void InsertFrameBefore()
    {
        Undo.Record(Document);
        Document.CurrentFrame = Document.InsertFrame(Document.CurrentFrame);
        Document.UpdateTransforms();
        Document.IncrementVersion();
    }

    private void InsertFrameAfter()
    {
        Undo.Record(Document);
        Document.CurrentFrame = Document.InsertFrame(Document.CurrentFrame + 1);
        Document.UpdateTransforms();
        Document.IncrementVersion();
    }

    private void InsertFrameAfterLerp()
    {
        Undo.Record(Document);
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        var prevFrame = Document.CurrentFrame;
        var newFrame = Document.InsertFrame(Document.CurrentFrame + 1);
        var nextFrame = (newFrame + 1) % Document.FrameCount;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            ref var newTransform = ref Document.GetFrameTransform(boneIndex, newFrame);
            ref var prevTransform = ref Document.GetFrameTransform(boneIndex, prevFrame);
            ref var nextTransform = ref Document.GetFrameTransform(boneIndex, nextFrame);

            newTransform.Position = Vector2.Lerp(prevTransform.Position, nextTransform.Position, 0.5f);
            newTransform.Rotation = prevTransform.Rotation + (nextTransform.Rotation - prevTransform.Rotation) * 0.5f;
            newTransform.Scale = Vector2.Lerp(prevTransform.Scale, nextTransform.Scale, 0.5f);
        }

        Document.CurrentFrame = newFrame;
        Document.UpdateTransforms();
        Document.IncrementVersion();
    }

    private void DeleteFrame()
    {
        Undo.Record(Document);
        Document.CurrentFrame = Document.DeleteFrame(Document.CurrentFrame);
        Document.UpdateTransforms();
        Document.IncrementVersion();
    }

    private void AddHoldFrame()
    {
        Undo.Record(Document);
        Document.Frames[Document.CurrentFrame].Hold++;
        Document.IncrementVersion();
    }

    private void RemoveHoldFrame()
    {
        if (Document.Frames[Document.CurrentFrame].Hold <= 0)
            return;

        Undo.Record(Document);
        Document.Frames[Document.CurrentFrame].Hold = Math.Max(0, Document.Frames[Document.CurrentFrame].Hold - 1);
        Document.IncrementVersion();
    }

    private void CopyKeys()
    {
        var frameData = new AnimationFrameData();
        for (var boneIndex = 0; boneIndex < Skeleton.MaxBones; boneIndex++)
            frameData.Transforms[boneIndex] = Document.Frames[Document.CurrentFrame].Transforms[boneIndex];
        Clipboard.Copy(frameData);
    }

    private void PasteKeys()
    {
        var frameData = Clipboard.Get<AnimationFrameData>();
        if (frameData == null)
            return;

        Undo.Record(Document);

        for (var boneIndex = 0; boneIndex < Skeleton.MaxBones; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;
            Document.Frames[Document.CurrentFrame].Transforms[boneIndex] = frameData.Transforms[boneIndex];
        }

        Document.IncrementVersion();
        Document.UpdateTransforms();
    }

    private void ToggleOnionSkin()
    {
        _onionSkin = !_onionSkin;
    }

    private void ToggleLoop()
    {
        Undo.Record(Document);
        Document.SetLooping(!Document.IsLooping);
        Document.IncrementVersion();
    }

    private void IncreasePlaySpeed()
    {
        _playSpeed = MathF.Min(_playSpeed + 0.1f, 4f);
    }

    private void DecreasePlaySpeed()
    {
        _playSpeed = MathF.Max(_playSpeed - 0.1f, 0.1f);
    }

    private void MirrorPose()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        Undo.Record(Document);

        var savedWorldTransforms = new Matrix3x2[Skeleton.MaxBones];
        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            savedWorldTransforms[boneIndex] = Document.LocalToWorld[boneIndex];

        for (var boneIndex = 1; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;

            var mirrorIndex = skeleton.GetMirrorBone(boneIndex);
            if (mirrorIndex == -1)
                continue;

            var bone = skeleton.Bones[boneIndex];
            var desiredWorldPos = Vector2.Transform(Vector2.Zero, savedWorldTransforms[mirrorIndex]);
            var desiredWorldRot = GetRotation(savedWorldTransforms[mirrorIndex]);

            var parentWorld = bone.ParentIndex >= 0
                ? Document.LocalToWorld[bone.ParentIndex]
                : Matrix3x2.Identity;

            Matrix3x2.Invert(parentWorld, out var invParent);
            var localPos = Vector2.Transform(desiredWorldPos, invParent);
            var framePos = localPos - bone.Transform.Position;

            var parentWorldRot = GetRotation(parentWorld);
            var frameRot = desiredWorldRot - parentWorldRot - bone.Transform.Rotation;

            ref var frame = ref Document.GetFrameTransform(boneIndex, Document.CurrentFrame);
            frame.Position = framePos;
            frame.Rotation = frameRot;

            Document.UpdateTransforms();
        }

        Document.IncrementVersion();
    }

    private static float GetRotation(Matrix3x2 transform)
    {
        return MathF.Atan2(transform.M12, transform.M11) * 180f / MathF.PI;
    }

    private void DrawBones()
    {
        if (!_showSkeleton) return;
        if (Document.Skeleton == null) return; 

        for (var boneIndex = 0; boneIndex < Document.Skeleton.BoneCount; boneIndex++)
        {
            var bone = Document.Skeleton.Bones[boneIndex];
            var animationBone = Document.Bones[boneIndex];
            var boneTransform = Document.LocalToWorld[boneIndex];
            var p0 = Vector2.Transform(Vector2.Zero, boneTransform);
            var p1 = Vector2.Transform(new Vector2(bone.Length, 0), boneTransform);

            Graphics.SetSortGroup((ushort)(animationBone.IsSelected ? 2 : 1));
            Gizmos.DrawBone(p0, p1, selected: animationBone.IsSelected);
        }
    }

    private void DrawEditor()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        var baseTransform = GetBaseTransform();

        using (Gizmos.PushState(EditorLayer.Document))
        {
            Graphics.SetTransform(baseTransform);
            Document.DrawOrigin();
            DrawOnionSkin();
            DrawBones();
            Graphics.SetSortGroup(0);
            Document.DrawSprites();
        }
    }

    private void DrawDopeSheet()
    {
    }

    private void DrawOnionSkin()
    {
        if (!_onionSkin || Document.FrameCount <= 1 || _state == AnimationEditorState.Play)
            return;

        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        var prevFrame = (Document.CurrentFrame - 1 + Document.FrameCount) % Document.FrameCount;
        var nextFrame = (Document.CurrentFrame + 1) % Document.FrameCount;
        var prevColor = new Color(1f, 0.3f, 0.3f, 0.3f);
        var nextColor = new Color(0.3f, 1f, 0.3f, 0.3f);

        DrawOnionSkinFrame(skeleton, prevFrame, prevColor);
        DrawOnionSkinFrame(skeleton, nextFrame, nextColor);

        Document.UpdateTransforms();
    }

    private void DrawOnionSkinFrame(SkeletonDocument skeleton, int frame, Color tint)
    {
        Document.UpdateTransforms(frame);

        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetSortGroup(1);

            for (var i = 0; i < skeleton.Attachments.Count; i++)
            {
                if (skeleton.Attachments[i] is not SpriteDocument sprite) continue;
                sprite.DrawSprite(skeleton.WorldToLocal, Document.LocalToWorld, Document.Transform, tint: tint);
            }
        }
    }

    public override void OnUndoRedo()
    {
        base.OnUndoRedo();
        Document.UpdateSkeleton();
        Document.UpdateTransforms();
    }
}
