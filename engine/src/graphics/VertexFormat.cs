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

public readonly struct RenderMesh(nuint handle, uint vertexHash)
{
    public readonly nuint Handle = handle;
    public readonly uint VertexHash = vertexHash;

    public static bool operator ==(RenderMesh a, RenderMesh b) => a.Handle == b.Handle;
    public static bool operator !=(RenderMesh a, RenderMesh b) => a.Handle != b.Handle;
    public override bool Equals(object? obj) => obj is RenderMesh m && Handle == m.Handle;
    public override int GetHashCode() => Handle.GetHashCode();
}

public static class VertexFormatHash
{
    /// <summary>
    /// Computes a hash from vertex attributes (location, components, type per attribute).
    /// Used to match shader VertexInput declarations against mesh vertex formats.
    /// </summary>
    public static uint Compute(ReadOnlySpan<(int location, int components, VertexAttribType type)> attributes)
    {
        uint hash = 2166136261u;
        foreach (var (location, components, type) in attributes)
        {
            hash = (hash ^ (uint)location) * 16777619u;
            hash = (hash ^ (uint)components) * 16777619u;
            hash = (hash ^ (uint)type) * 16777619u;
        }
        return hash;
    }

    public static uint Compute(VertexAttribute[] attributes)
    {
        uint hash = 2166136261u;
        foreach (var attr in attributes)
        {
            // Normalized UByte is presented as Float to shaders (WebGPU unorm conversion)
            var type = attr is { Type: VertexAttribType.UByte, Normalized: true } ? VertexAttribType.Float : attr.Type;
            hash = (hash ^ (uint)attr.Location) * 16777619u;
            hash = (hash ^ (uint)attr.Components) * 16777619u;
            hash = (hash ^ (uint)type) * 16777619u;
        }
        return hash;
    }
}
