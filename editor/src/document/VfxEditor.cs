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
        public static partial WidgetId OutlinerSplitter { get; }
        public static partial WidgetId EmitterRow { get; }
        public static partial WidgetId ParticleRow { get; }
        public static partial WidgetId AddEmitterButton { get; }
        public static partial WidgetId AddParticleButton { get; }
        public static partial WidgetId RenameInput { get; }
        public static partial WidgetId VfxRoot { get; }
    }

    private float _outlinerSize = 180f;

    public override bool ShowInspector => true;
    public override bool ShowInIsolation => true;
    public override bool RunInBackground => Document.IsPlaying;

    public new VfxDocument Document => (VfxDocument)base.Document;

    public VfxEditor(VfxDocument document) : base(document)
    {
        Commands =
        [
            new Command { Name = "Toggle Playback", Handler = TogglePlayback, Key = InputCode.KeySpace },
            new Command { Name = "Rotate", Handler = BeginRotateTool, Key = InputCode.KeyR },
            new Command { Name = "Delete", Handler = DeleteSelected, Key = InputCode.KeyX },
            new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab },
        ];

        Document.Play();
    }

    public override void Dispose()
    {
        base.Dispose();
        Document.Stop();
    }

    // --- Main UI layout ---

    public override void UpdateUI()
    {
        using (UI.BeginRow())
        {
            using (UI.BeginFlex())
                OutlinerUI();

            UI.FlexSplitter(ElementId.OutlinerSplitter, ref _outlinerSize,
                EditorStyle.Inspector.Splitter, fixedPane: 1);

            using (UI.BeginFlex()) { }
        }
    }

    public override void Update()
    {
        using (Gizmos.PushState(EditorLayer.Document))
        {
            Graphics.SetTransform(Document.Transform);
            Document.DrawOrigin();
            Document.DrawBounds(selected: false);

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
