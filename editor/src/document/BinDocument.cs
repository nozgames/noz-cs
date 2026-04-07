//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class BinDocument : Document
{
    public const string Extension = ".bin";

    public static void RegisterDef()
    {
        DocumentDef<BinDocument>.Register(new DocumentDef
        {
            Type = AssetType.Bin,
            Name = "Bin",
            Extensions = [Extension],
            Factory = _ => new BinDocument(),
            Icon = () => EditorAssets.Sprites.AssetIconBin
        });
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        var data = EditorApplication.Store.ReadAllBytes(Path);
        using var writer = new BinaryWriter(EditorApplication.Store.OpenWrite(outputPath));
        writer.WriteAssetHeader(AssetType.Bin, Bin.Version);
        writer.Write(data.Length);
        writer.Write(data);
    }

    public override void Draw()
    {
        using (Graphics.PushState())
        {
            Graphics.SetShader(EditorAssets.Shaders.Sprite);
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetColor(Color.White);
            Graphics.Draw(EditorAssets.Sprites.AssetIconBin);
        }
    }
}
