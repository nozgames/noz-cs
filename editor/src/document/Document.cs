//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public abstract class Document : IDisposable, IChangeHandler
{
    public DocumentDef Def { get; internal set; } = null!;
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string CollectionId { get; set; } = "";
    public virtual bool IsPlaying { get; } = false;
    public virtual bool CanPlay => false;
    public virtual bool CanSave => false;

    public Vector2 Position { get; set; }
    public Vector2 SavedPosition { get; set; }
    public Rect Bounds { get; set; } = new(-0.5f, -0.5f, 1f, 1f);

    public int Version { get; private set; }
    public int SavedVersion { get; private set; }

    public Matrix3x2 Transform => Matrix3x2.CreateTranslation(Position);
    
    public bool IsHiddenInWorkspace { get; set; }
    public bool IsQueuedForExport { get; internal set; }
    public bool IsDisposed { get; private set; }
    public bool IsVisible { get; set; } = true;
    public bool IsSelected { get; set; }
    public bool IsEditing { get; set; }
    public bool IsModified => Version != SavedVersion;
    public bool IsClipped { get; set; }
    public bool Loaded { get; set; }
    public bool PostLoaded { get; set; }
    public bool ShouldExport { get; set; } = true;
    public bool SilentExport { get; set; }

    public virtual void Load() { }
    public virtual void Save(StreamWriter sw) { }
    public virtual void Reload() { }
    public virtual void PostLoad() { }
    public virtual void LoadMetadata(PropertySet meta) { }
    public virtual void SaveMetadata(PropertySet meta) { }
    public virtual void Export(string outputPath, PropertySet meta) { }
    public virtual void GetDependencies(List<(AssetType Type, string Name)> dependencies) { }
    public virtual void GetReferences(List<Document> references) { }
    public virtual void Draw() { }
    public virtual bool DrawThumbnail() => false;
    public virtual void Clone(Document source) { }
    public virtual void OnUndoRedo() { }

    public virtual void Play() { }
    public virtual void Stop() { }

    public void TogglePlay()
    {
        if (!CanPlay) return;
        if (IsPlaying)
            Stop();
        else
            Play();
    }

    public void SaveMetadata()
    {
        var metaPath = Path + ".meta";
        var props = PropertySet.LoadFile(metaPath) ?? new PropertySet();

        props.SetVec2("editor", "position", Position);
        if (!string.IsNullOrEmpty(CollectionId))
            props.SetString("editor", "collection", CollectionId);
        if (!ShouldExport)
            props.SetBool("editor", "export", false);
        SaveMetadata(props);
        props.Save(metaPath);
    }

    public void LoadMetadata()
    {
        var props = PropertySet.LoadFile(Path + ".meta") ?? new PropertySet();  

        Position = props.GetVector2("editor", "position", default);
        var collectionId = props.GetString("editor", "collection", "");
        CollectionId = CollectionManager.GetIdOrDefault(collectionId);
        ShouldExport = props.GetBool("editor", "export", true);
        LoadMetadata(props);        
    }

    public void Save() 
    {
        SavedVersion = Version;

        if (!CanSave)
            return;

        using var sw = new StreamWriter(Path);
        Save(sw);
    }

    void IChangeHandler.BeginChange() => Undo.Record(this);
    void IChangeHandler.NotifyChange() => IncrementVersion();
    void IChangeHandler.CancelChange() => Undo.Cancel();

    public void IncrementVersion()
    {
        Version++;
    }

    public virtual void Dispose () 
    {
        IsDisposed = true;
    }

    public void DrawBounds(bool selected=false)
    {
        using (Gizmos.PushState(selected ? EditorLayer.Selection : EditorLayer.Document))
        {
            Graphics.SetTransform(Transform);
            Graphics.SetColor(selected
                ? EditorStyle.Workspace.SelectionColor
                : EditorStyle.Workspace.BoundsColor);
            Gizmos.DrawRect(Bounds, EditorStyle.Workspace.DocumentBoundsLineWidth, outside: true);
        }
    }

    public void DrawOrigin()
    {
        var selected = IsSelected || IsEditing;
        using (Gizmos.PushState(selected ? EditorLayer.Selection : EditorLayer.Document))
            Gizmos.DrawOrigin(selected
                ? EditorStyle.Workspace.OriginColor
                : EditorStyle.Workspace.BoundsColor,
                order: 1);
    }

    public void Reexport()
    {
        if (File.Exists(Path))
            File.SetLastWriteTimeUtc(Path, DateTime.UtcNow);
    }
}
