using System.Numerics;

namespace NoZ.Editor.Graphics3D;

public sealed class Vfx3DDocument : Document
{
    public const string Extension = ".vfx3d";
    public Vfx3DSource Source { get; private set; } = new();
    public int PreviewRevision { get; private set; }
    public override bool CanSave => true;

    public static void RegisterDef() => DocumentDef<Vfx3DDocument>.Register(new DocumentDef
    {
        Type = Vfx3D.Type, Name = "Vfx3D", Extensions = [Extension],
        Factory = _ => new Vfx3DDocument(), EditorFactory = doc => new Vfx3DEditor((Vfx3DDocument)doc),
        CreateNew = position => CreateNew(position: position), Icon = () => EditorAssets.Sprites.AssetIconVfx
    });
    public static Document? CreateNew(string? name = null, Vector2? position = null) =>
        Project.New(Vfx3D.Type, Extension, name, (StreamWriter writer) => writer.Write(new Vfx3DSource().ToJson()), position);

    public override void Load() => ReadSource();
    public override void Reload() => ReadSource();
    private void ReadSource()
    {
        var source = Vfx3DSource.Parse(File.ReadAllText(Path));
        Source = source; PreviewRevision++;
    }
    public override void Save(StreamWriter sw) { Source.Bake(); sw.Write(Source.ToJson()); }
    public override void Export(string outputPath, PropertySet meta)
    {
        var source = Vfx3DSource.Parse(File.ReadAllText(Path));
        using var stream = File.Create(outputPath);
        Vfx3D.Write(stream, source.Loop, source.Bake());
    }
    public override void Clone(Document source)
    {
        Source = Vfx3DSource.Parse(((Vfx3DDocument)source).Source.ToJson()); PreviewRevision++;
    }
    public override void OnUndoRedo() => PreviewRevision++;
    public void ApplyChanges() { IncrementVersion(); PreviewRevision++; }
    public override void GetDependencies(List<(AssetType Type, string Name)> dependencies)
    {
        dependencies.Add((AssetType.Shader, "vfx3d"));
        foreach (var emitter in Source.Emitters)
            if (!string.IsNullOrEmpty(emitter.Texture)) dependencies.Add((AssetType.Texture, emitter.Texture));
    }
    public override void GetReferences(List<Document> references)
    {
        foreach (var document in Project.Documents)
            if (document.Def.Type == AssetType.Texture && Source.Emitters.Any(e => e.Texture == document.Name)) references.Add(document);
    }
    public override void OnRenamed(Document doc, string oldName, string newName)
    {
        if (doc.Def.Type != AssetType.Texture) return;
        var changed = false;
        foreach (var emitter in Source.Emitters)
            if (emitter.Texture == oldName) { emitter.Texture = newName; changed = true; }
        if (changed) ApplyChanges();
    }
    public override void Draw()
    {
        using var state = Graphics.PushState();
        Graphics.SetShader(EditorAssets.Shaders.Sprite); Graphics.SetColor(Color.White);
        Graphics.SetLayer(EditorLayer.Document); Graphics.Draw(EditorAssets.Sprites.AssetIconVfx);
    }
}
