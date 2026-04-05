//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static class PropertySetExtensions
{
    public static PropertySet? LoadFile(IEditorStore store, string path) =>
        store.FileExists(path) ? PropertySet.Load(store.ReadAllText(path)) : null;

    public static void Save(this PropertySet props, string path, IEditorStore store) =>
        store.WriteAllText(path, props.SaveToString());
}
