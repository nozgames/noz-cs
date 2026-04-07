//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class DocumentDef
{
    public required AssetType Type { get; init; }
    public required string Name { get; init; }
    public required string[] Extensions { get; init; }
    public required Func<string?, Document> Factory { get; init; }
    public Action<StreamWriter>? NewFile { get; init; }
    public Func<Document, DocumentEditor>? EditorFactory { get; init; }
    public string[]? AuxiliaryExtensions { get; init; }
    public Func<Document, bool>? CanEdit { get; init; }
    public Func<Sprite?>? Icon { get; init; }
}

public static class DocumentDef<T> where T : Document
{
    public static DocumentDef Def { get; private set; } = null!;

    public static void Register(DocumentDef def)
    {
        Def = def;
        DocumentManager.RegisterDef(def);
    }
}
