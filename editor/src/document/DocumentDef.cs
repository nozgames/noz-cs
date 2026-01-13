namespace noz.editor;

public class DocumentDef
{
    public AssetType Type { get; }
    public int Size { get; }
    public string Extension { get; }
    public Func<Document> Factory { get; }

    private static readonly Dictionary<AssetType, DocumentDef> _byType = new();
    private static readonly Dictionary<string, DocumentDef> _byExtension = new();

    public DocumentDef(AssetType type, string extension, Func<Document> factory)
    {
        Type = type;
        Extension = extension.ToLowerInvariant();
        Factory = factory;
    }

    public static void Register(DocumentDef def)
    {
        _byType[def.Type] = def;
        _byExtension[def.Extension] = def;
    }

    public static DocumentDef? GetByType(AssetType type)
    {
        return _byType.TryGetValue(type, out var def) ? def : null;
    }

    public static DocumentDef? GetByExtension(string ext)
    {
        ext = ext.ToLowerInvariant();
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        return _byExtension.TryGetValue(ext, out var def) ? def : null;
    }

    public static IEnumerable<DocumentDef> All => _byType.Values;
}
