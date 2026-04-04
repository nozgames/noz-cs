//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal partial class VfxEditor : DocumentEditor
{
    private static partial class ElementId
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId ToolbarRoot { get; }
        public static partial WidgetId PlayButton { get; }
        public static partial WidgetId LoopButton { get; }
        public static partial WidgetId OutlinerPanel { get; }
        public static partial WidgetId EmitterRow { get; }
        public static partial WidgetId ParticleRow { get; }
        public static partial WidgetId AddEmitterButton { get; }
        public static partial WidgetId AddParticleButton { get; }
        public static partial WidgetId RenameInput { get; }
        public static partial WidgetId VfxRoot { get; }
    }

    public override bool ShowInspector => true;
    public override bool ShowOutliner => true;
    public override bool RunInBackground => Document.IsPlaying;

    public new VfxDocument Document => (VfxDocument)base.Document;

    public VfxEditor(VfxDocument document) : base(document)
    {
        Commands =
        [
            new Command ("Toggle Playback", TogglePlayback,         [InputCode.KeySpace]),
            new Command ("Rotate",          BeginRotateTool,        [InputCode.KeyR]),
            new Command ("Delete",          DeleteSelected,         [InputCode.KeyX, InputCode.KeyDelete]),
            new Command ("Duplicate",       DuplicateSelected,      [new KeyBinding(InputCode.KeyD, ctrl:true)]),
            new Command ("Copy",            CopySelected,           [new KeyBinding(InputCode.KeyC, ctrl:true)]),
            new Command ("Paste",           PasteFromClipboard,     [new KeyBinding(InputCode.KeyV, ctrl:true)]),
            new Command ("Cut",             CutSelected,            [new KeyBinding(InputCode.KeyX, ctrl:true)]),
            new Command ("Exit Edit Mode",  Workspace.EndEdit,      [InputCode.KeyTab]),
            new Command ("Frame",           FrameVfxBounds,         [InputCode.KeyF]),
        ];

        Document.Play();
    }

    public override void Dispose()
    {
        base.Dispose();
        Document.Stop();
    }

    public override void UpdateUI() { }


    public override void Update()
    {
        using (Gizmos.PushState(EditorLayer.Document))
        {
            Graphics.SetTransform(Document.Transform);
            Document.DrawOrigin();
            Gizmos.SetColor(EditorStyle.Workspace.BoundsColor);
            Gizmos.DrawRect(Document.VfxBounds, EditorStyle.Workspace.DocumentBoundsLineWidth, outside: true);

            if (Document.SelectedEmitters.Count > 0)
            {
                var rotTransform = Matrix3x2.CreateRotation(MathEx.Deg2Rad * Document.Rotation) *
                                   Matrix3x2.CreateTranslation(Document.Position);
                foreach (var emitterIndex in Document.SelectedEmitters)
                {
                    if (emitterIndex >= 0 && emitterIndex < Document.Emitters.Count)
                    {
                        Graphics.SetTransform(rotTransform);
                        DrawSpawnGizmo(ref Document.Emitters[emitterIndex].Def.Spawn);
                    }
                }
                Graphics.SetTransform(Document.Transform);
            }

            if (Document.Rotation != 0f && Workspace.ActiveTool == null)
            {
                var rotTransform = Matrix3x2.CreateRotation(MathEx.Deg2Rad * Document.Rotation) *
                                   Matrix3x2.CreateTranslation(Document.Position);
                Graphics.SetTransform(rotTransform);
                Gizmos.SetColor(Color.Black.WithAlpha(0.3f));
                Gizmos.DrawDashedLine(Vector2.Zero, new Vector2(2f, 0f));
            }
        }

        Graphics.SetTransform(Document.Transform);
        Document.Draw();
    }

    public override void UpdateOverlayUI()
    {
        using (FloatingToolbar.Begin())
        {
            if (FloatingToolbar.Button(ElementId.PlayButton, EditorAssets.Sprites.IconPlay, isSelected: Document.IsPlaying))
                TogglePlayback();

            if (FloatingToolbar.Button(ElementId.LoopButton, EditorAssets.Sprites.IconLoop, isSelected: Document.EditorLoop))
                Document.EditorLoop = !Document.EditorLoop;
        }
    }

    private void TogglePlayback()
    {
        Document.TogglePlay();
    }

    private void FrameVfxBounds()
    {
        Workspace.FrameRect(Document.VfxBounds.Translate(Document.Position));
    }

    private static void DrawSpawnGizmo(ref VfxSpawnDef spawn)
    {
        const float lineWidth = EditorStyle.Workspace.DocumentBoundsLineWidth;
        Gizmos.SetColor(EditorStyle.Workspace.SelectionColor);

        switch (spawn.Shape)
        {
            case VfxSpawnShape.Point:
                const float crossSize = 0.05f;
                Gizmos.DrawLine(spawn.Offset - new Vector2(crossSize, 0), spawn.Offset + new Vector2(crossSize, 0), lineWidth);
                Gizmos.DrawLine(spawn.Offset - new Vector2(0, crossSize), spawn.Offset + new Vector2(0, crossSize), lineWidth);
                break;

            case VfxSpawnShape.Circle:
                Gizmos.DrawArc(spawn.Offset, spawn.Circle.Radius, 0, MathF.Tau, lineWidth);
                if (spawn.Circle.InnerRadius > 0 && spawn.Circle.InnerRadius < spawn.Circle.Radius)
                    Gizmos.DrawArc(spawn.Offset, spawn.Circle.InnerRadius, 0, MathF.Tau, lineWidth);
                break;

            case VfxSpawnShape.Box:
                var half = spawn.Box.Size * 0.5f;
                var rect = new Rect(spawn.Offset.X - half.X, spawn.Offset.Y - half.Y, spawn.Box.Size.X, spawn.Box.Size.Y);
                if (MathF.Abs(spawn.Box.Rotation) > 0.0001f)
                    Gizmos.DrawRotatedRect(rect, spawn.Offset, MathEx.Radians(spawn.Box.Rotation), lineWidth);
                else
                    Gizmos.DrawRect(rect, lineWidth);

                if (spawn.Box.InnerSize.X > 0 || spawn.Box.InnerSize.Y > 0)
                {
                    var innerHalf = spawn.Box.InnerSize * 0.5f;
                    var innerRect = new Rect(spawn.Offset.X - innerHalf.X, spawn.Offset.Y - innerHalf.Y, spawn.Box.InnerSize.X, spawn.Box.InnerSize.Y);
                    if (MathF.Abs(spawn.Box.Rotation) > 0.0001f)
                        Gizmos.DrawRotatedRect(innerRect, spawn.Offset, MathEx.Radians(spawn.Box.Rotation), lineWidth);
                    else
                        Gizmos.DrawRect(innerRect, lineWidth);
                }
                break;
        }
    }

    private void BeginRotateTool()
    {
        var pivotWorld = Vector2.Transform(Vector2.Zero, Document.Transform);
        var invTransform = Matrix3x2.Identity;
        Matrix3x2.Invert(Document.Transform, out invTransform);
        var savedRotation = Document.Rotation;

        Undo.Record(Document);

        Workspace.BeginTool(new RotateTool(
            pivotWorld,
            Vector2.Zero,
            pivotWorld,
            Vector2.Zero,
            invTransform,
            update: _ => { Document.Rotation = GetMouseAngle(); },
            commit: _ => { Document.Rotation = GetMouseAngle(); },
            cancel: () => { Undo.Cancel(); }
        ));
    }

    private float GetMouseAngle()
    {
        var dir = Workspace.MouseWorldPosition - Document.Position;
        var angle = MathEx.Rad2Deg * MathF.Atan2(dir.Y, dir.X);
        if (Input.IsCtrlDown(InputScope.All))
            angle = MathF.Round(angle / 15f) * 15f;
        return angle;
    }
}
