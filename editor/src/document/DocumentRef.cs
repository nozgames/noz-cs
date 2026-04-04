//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public struct DocumentRef<T> where T : Document
{
    public string? Name;
    public T? Value;

    public bool HasValue => !string.IsNullOrEmpty(Name);
    public bool IsResolved => Value != null;

    public static implicit operator T? (DocumentRef<T> docRef) => docRef.Value;
    public static explicit operator T? (DocumentRef<T>? doc) => doc as T;

    public static bool operator== (DocumentRef<T> a, T? value) => a.Value == value;
    public static bool operator!=(DocumentRef<T> a, T? value) => a.Value != value;

    public static implicit operator DocumentRef<T>(T? doc) => new() { Value = doc, Name = doc?.Name };

    public void Resolve()
    {
        Value = string.IsNullOrEmpty(Name) ? null : DocumentManager.Find<T>(Name);
    }

    public bool TryRename(string oldName, string newName)
    {
        if (!string.Equals(Name, oldName, StringComparison.OrdinalIgnoreCase))
            return false;
        Name = newName;
        Resolve();
        return true;
    }

    public void Clear()
    {
        Value = null;
        Name = null;
    }

    public override readonly bool Equals(object? obj)
    {
        if (obj is DocumentRef<T> docRef)
            return this == docRef;
        if (obj is T doc)
            return this == doc;
        return false;
    }

    public override readonly int GetHashCode()
    {
        return Value?.GetHashCode() ?? 0;
    }
}
