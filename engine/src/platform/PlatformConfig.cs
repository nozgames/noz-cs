//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Platform;

public class PlatformConfig
{
    public string Title { get; init; } = "Noz";
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public bool VSync { get; init; } = true;
    public bool Resizable { get; init; } = true;
    public string? IconPath { get; init; }
    public int MsaaSamples { get; init; } = 4;
}