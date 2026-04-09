//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static class UserSettings
{
    private const string UserConfigPath = ".noz/user.cfg";

    public static void Load()
    {
        var store = EditorApplication.Store;
        var props = PropertySetExtensions.LoadFile(store, UserConfigPath);
        if (props == null)
            return;

        CollectionManager.LoadUserSettings(props);
        Workspace.LoadUserSettings(props);
        VectorSpriteEditor.LoadUserSettings(props);
        EditorApplication.AppConfig.LoadUserSettings?.Invoke(props);
    }

    public static void Save()
    {
        var store = EditorApplication.Store;
        var dir = Path.GetDirectoryName(UserConfigPath);
        if (!string.IsNullOrEmpty(dir) && !store.DirectoryExists(dir))
            store.CreateDirectory(dir);

        var props = new PropertySet();
        Workspace.SaveUserSettings(props);
        CollectionManager.SaveUserSettings(props);
        VectorSpriteEditor.SaveUserSettings(props);
        EditorApplication.AppConfig.SaveUserSettings?.Invoke(props);
        props.Save(UserConfigPath, store);
    }
}
