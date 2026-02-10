//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal partial class VfxEditor : DocumentEditor
{
    [ElementId("Root")]
    [ElementId("PlayButton")]
    [ElementId("LoopButton")]
    private static partial class ElementId { }

    public new VfxDocument Document => (VfxDocument)base.Document;

    public VfxEditor(VfxDocument document) : base(document)
    {
        Commands =
        [
            new Command { Name = "Toggle Playback", Handler = TogglePlayback, Key = InputCode.KeySpace },
            new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab },
        ];
    }

    public override void Update()
    {
        using (Gizmos.PushState(EditorLayer.Document))
        {
            Graphics.SetTransform(Document.Transform);
            Document.DrawOrigin();
            Document.DrawBounds(selected: false);
        }

        Graphics.SetTransform(Document.Transform);
        Document.Draw();
    }

    public override void UpdateUI()
    {
        using (UI.BeginColumn(ElementId.Root, EditorStyle.DocumentEditor.Root))
        {
            ToolbarUI();
        }
    }

    private void ToolbarUI()
    {
        using var _ = UI.BeginRow(EditorStyle.Toolbar.Root);

        UI.Flex();

        if (EditorUI.Button(ElementId.PlayButton, EditorAssets.Sprites.IconPlay, selected: Document.IsPlaying, toolbar: true))
            TogglePlayback();

        UI.Flex();
    }

    private void TogglePlayback()
    {
        Document.TogglePlay();
    }
}
