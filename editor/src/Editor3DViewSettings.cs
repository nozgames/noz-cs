namespace NoZ.Editor;

/// <summary>User preferences shared by mesh and prop editing views.</summary>
public static class Editor3DViewSettings
{
    public static float Yaw { get; set; } = MathF.PI / 4;
    public static float Pitch { get; set; } = MathF.PI * .15f;
    public static bool Perspective { get; set; }
    public static bool ShowBounds { get; set; } = true;
    public static bool ShowLightRange { get; set; }
    public static bool PreviewLight { get; set; }

    internal static void LoadUserSettings(PropertySet props)
    {
        var yaw = props.GetFloat("view_3d", "yaw", MathF.PI / 4);
        var pitch = props.GetFloat("view_3d", "pitch", MathF.PI * .15f);
        Yaw = float.IsFinite(yaw) ? MathF.IEEERemainder(yaw, MathF.Tau) : MathF.PI / 4;
        Pitch = float.IsFinite(pitch) ? Math.Clamp(pitch, -MathF.PI / 2, MathF.PI / 2) : MathF.PI * .15f;
        Perspective = props.GetBool("view_3d", "perspective", false);
        ShowBounds = props.GetBool("view_3d", "show_bounds", true);
        ShowLightRange = props.GetBool("view_3d", "show_light_range", false);
        PreviewLight = props.GetBool("view_3d", "preview_light", false);
    }

    internal static void SaveUserSettings(PropertySet props)
    {
        props.SetFloat("view_3d", "yaw", Yaw);
        props.SetFloat("view_3d", "pitch", Pitch);
        props.SetBool("view_3d", "perspective", Perspective);
        props.SetBool("view_3d", "show_bounds", ShowBounds);
        props.SetBool("view_3d", "show_light_range", ShowLightRange);
        props.SetBool("view_3d", "preview_light", PreviewLight);
    }
}
