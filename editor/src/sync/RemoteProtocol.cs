//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text.Json.Serialization;

namespace NoZ.Editor;

public class FileEntryDto
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("mtime")] public long MtimeTicks { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}

public class ListResponseDto
{
    [JsonPropertyName("files")] public List<FileEntryDto> Files { get; set; } = [];
}

public class InfoResponseDto
{
    [JsonPropertyName("root")] public string Root { get; set; } = "";
    [JsonPropertyName("projectName")] public string ProjectName { get; set; } = "";
    [JsonPropertyName("syncPaths")] public string[] SyncPaths { get; set; } = [];
    [JsonPropertyName("serverId")] public string ServerId { get; set; } = "";
    [JsonPropertyName("protocol")] public int Protocol { get; set; } = RemoteProtocol.Version;
}

public class EventDto
{
    [JsonPropertyName("op")] public string Op { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("srcPath")] public string? SrcPath { get; set; }
    [JsonPropertyName("mtime")] public long MtimeTicks { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ListResponseDto))]
[JsonSerializable(typeof(InfoResponseDto))]
[JsonSerializable(typeof(EventDto))]
[JsonSerializable(typeof(FileEntryDto))]
public partial class RemoteJsonContext : JsonSerializerContext;

public static class RemoteProtocol
{
    public const int Version = 1;
    public const int DefaultPort = 5050;

    public const string HeaderMtime = "X-Mtime-Ticks";
    public const string HeaderSize = "X-Size";
    public const string HeaderClientId = "X-Client-Id";

    public const string OpChanged = "changed";
    public const string OpDeleted = "deleted";
    public const string OpMoved = "moved";
}
