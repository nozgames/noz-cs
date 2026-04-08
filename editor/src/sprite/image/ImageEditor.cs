//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal partial class ImageEditor : DocumentEditor
{
    public new ImageSpriteDocument Document => (ImageSpriteDocument)base.Document;

    public ImageEditor(ImageSpriteDocument doc) : base(doc)
    {
        Commands =
        [
            new Command("Exit Edit Mode", Workspace.EndEdit, [InputCode.KeyTab])
        ];
    }

    public override void Update()
    {
        Graphics.SetTransform(Document.Transform);
        Document.DrawBounds(selected: false);
        Document.Draw();
    }
}
