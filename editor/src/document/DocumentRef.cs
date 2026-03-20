//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public struct DocumentRef<T> where T : Document
{
    public string? Name;
    public T? Document;

    public bool IsSet => !string.IsNullOrEmpty(Name);
    public bool IsResolved => Document != null;

    public void Resolve()
    {
        Document = string.IsNullOrEmpty(Name) ? null : DocumentManager.Find<T>(Name);
    }

    public void Set(T? doc)
    {
        Document = doc;
        Name = doc?.Name;
    }

    public void Clear()
    {
        Document = null;
        Name = null;
    }
}
