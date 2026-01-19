//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ.Platform;

namespace NoZ;

public interface IVertex
{
    static abstract VertexFormatDescriptor GetFormatDescriptor();
}

public static class VertexFormat<T> where T : unmanaged, IVertex
{
}

public enum VertexAttribType
{
    Float,
    Int,
    UByte
}

public struct VertexAttribute(int location, int components, VertexAttribType type, int offset, bool normalized = false)
{
    public readonly int Location = location;
    public readonly int Components = components;
    public readonly VertexAttribType Type = type;
    public readonly int Offset = offset;
    public readonly bool Normalized = normalized;
}
