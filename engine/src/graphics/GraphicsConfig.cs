//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ.Platform;

namespace NoZ;

public class GraphicsConfig 
{
    public const int DefaultMaxGlobalSnapshots = 1024;
    public const int DefaultMaxMeshes = 256;

    public bool Vsync { get; init; } = true;
    public int MaxDrawCommands { get; init; } = 16384;
    public int MaxBatches { get; init; } = 4096;
    /// <summary>
    /// Maximum number of unique projection/draw-parameter snapshots that may be
    /// submitted in one frame. Buffers are created lazily as they are used.
    /// </summary>
    public int MaxGlobalSnapshots { get; init; } = DefaultMaxGlobalSnapshots;
    /// <summary>
    /// Maximum number of GPU mesh resources that may be alive at once.
    /// </summary>
    public int MaxMeshes { get; init; } = DefaultMaxMeshes;
    public required IGraphicsDriver Driver { get; init; }
    public float PixelsPerUnit { get; init; } = 64.0f;
    public bool HDR { get; init; } = false;
}
