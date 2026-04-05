//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum SpriteFillType : byte
{
    Solid,
    Linear,
    // Radial,  // future
}

public struct SpriteFillGradient
{
    public Vector2 Start;      // path-local coordinates
    public Vector2 End;        // path-local coordinates
    public Color32 StartColor;
    public Color32 EndColor;
}
