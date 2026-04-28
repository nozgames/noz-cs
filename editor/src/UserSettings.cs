//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static class UserSettings
{
    public const string Path = ".noz/project.cfg";

    public static PropertySet? LoadPropertySet() =>
        PropertySet.LoadFile(System.IO.Path.Combine(Project.Path, Path));

    public static void Load()
    {
        var props = LoadPropertySet();
        if (props == null)
            return;

        CollectionManager.LoadUserSettings(props);
        Workspace.LoadUserSettings(props);
        VectorSpriteEditor.LoadUserSettings(props);
        PixelEditor.LoadUserSettings(props);
        EditorApplication.LoadUserSettings(props);
    }

    public static void Save()
    {
        var props = new PropertySet();
        Workspace.SaveUserSettings(props);
        CollectionManager.SaveUserSettings(props);
        VectorSpriteEditor.SaveUserSettings(props);
        PixelEditor.SaveUserSettings(props);
        EditorApplication.SaveUserSettings(props);
        props.Save(System.IO.Path.Combine(Project.Path, Path));
    }
}
