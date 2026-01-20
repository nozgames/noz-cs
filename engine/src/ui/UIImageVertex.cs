//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

[StructLayout(LayoutKind.Sequential)]
internal struct UIImageVertex : IVertex
{
    public Vector2 Position;
    public Vector2 UV;
    public Color Color;
    public int AtlasIndex;

    public static readonly int SizeInBytes = Marshal.SizeOf<UIImageVertex>();

    public static VertexFormatDescriptor GetFormatDescriptor() => new()
    {
        Stride = SizeInBytes,
        Attributes =
        [
            new VertexAttribute(0, 2, VertexAttribType.Float, (int)Marshal.OffsetOf<UIImageVertex>(nameof(Position))),
            new VertexAttribute(1, 2, VertexAttribType.Float, (int)Marshal.OffsetOf<UIImageVertex>(nameof(UV))),
            new VertexAttribute(2, 4, VertexAttribType.Float, (int)Marshal.OffsetOf<UIImageVertex>(nameof(Color))),
            new VertexAttribute(3, 1, VertexAttribType.Int, (int)Marshal.OffsetOf<UIImageVertex>(nameof(AtlasIndex)))
        ]
    };
}
