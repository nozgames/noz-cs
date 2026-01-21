//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class DocumentDef(
    AssetType type,
    string extension,
    Func<Document> factory, Func<Document, DocumentEditor>? editorFactory = null,
    Action<StreamWriter>? newFile = null)
{
    public AssetType Type { get; } = type;
    public string Extension { get; } = extension.ToLowerInvariant();
    public Func<Document> Factory { get; } = factory;
    public Action<StreamWriter>? NewFile { get; } = newFile;
    public Func<Document, DocumentEditor>? EditorFactory { get; } = editorFactory;

    public bool CanEdit => EditorFactory != null;
}
