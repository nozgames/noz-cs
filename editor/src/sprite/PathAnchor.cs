//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

[Flags]
public enum SpritePathAnchorFlags : byte
{
    None = 0,
    Selected = 1 << 0,
}

public struct SpritePathAnchor
{
    public Vector2 Position;
    public float Curve;
    public SpritePathAnchorFlags Flags;

    public readonly bool IsSelected => (Flags & SpritePathAnchorFlags.Selected) != 0;
}
