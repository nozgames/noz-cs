using NoZ.Editor;

namespace NoZ.Editor.Graphics3D;

public static class MeshStatisticsInspector
{
    public static void DrawChannels(MeshChannels channels, string? unavailable = null)
    {
        using var property = EditorInspector.BeginProperty("Channels", labelAlignY: Align.Min);
        using var column = UI.BeginColumn();
        if (unavailable != null || channels == MeshChannels.Unknown)
        {
            UI.Text(unavailable ?? "Unknown (reimport)", EditorStyle.Text.Secondary);
            return;
        }
        foreach (var channel in Enum.GetValues<MeshChannels>())
            if (channel != MeshChannels.Unknown && (channels & channel) != 0)
                UI.Text(channel == MeshChannels.Color ? "Vertex color" : channel.ToString(), EditorStyle.Text.Primary);
    }
}
