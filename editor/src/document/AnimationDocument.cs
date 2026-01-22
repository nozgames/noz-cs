//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal class AnimationDocument : Document
{
    public AnimationDocument()
    {
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef(
            AssetType.Sprite,
            ".anim",
            () => new AnimationDocument(),
            doc => new AnimationEditor((AnimationDocument)doc)
        ));
    }
}
