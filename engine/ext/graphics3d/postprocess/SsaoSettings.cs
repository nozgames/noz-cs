//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

/// <summary>Caller-owned SSAO tuning. Defaults preserve the original Cozy appearance.</summary>
public sealed class SsaoSettings
{
    public bool Enabled { get; set; } = true;
    public float Radius { get; set; } = 1.25f;
    public float Strength { get; set; } = 4.25f;
    public float Bias { get; set; } = 0.06f;
    public float SurfaceThickness { get; set; } = 0.05f;
    public float NoiseFloor { get; set; } = 0.05f;
    public float MaxDarkening { get; set; } = 0.3f;
    public float ResolutionScale { get; set; } = 0.5f;
    /// <summary>Displays the filtered AO factor as grayscale.</summary>
    public bool DebugView { get; set; }
}
