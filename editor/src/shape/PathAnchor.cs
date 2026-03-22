//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

[Flags]
public enum AnchorFlags : byte
{
    None = 0,
    Selected = 1 << 0,
}

public struct PathAnchor
{
    public Vector2 Position;
    public float Curve;
    public AnchorFlags Flags;

    public readonly bool IsSelected => (Flags & AnchorFlags.Selected) != 0;
}
