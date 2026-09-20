//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Platform;

/// <summary>Vertex layout by value. Drivers copy the attributes when creating a mesh.</summary>
public struct VertexFormatDescriptor : IEquatable<VertexFormatDescriptor>
{
    public VertexAttribute[] Attributes;
    public int Stride;

    public readonly bool Equals(VertexFormatDescriptor other)
    {
        if (Stride != other.Stride) return false;
        if (ReferenceEquals(Attributes, other.Attributes)) return true;
        var count = Attributes?.Length ?? 0;
        if (count != (other.Attributes?.Length ?? 0)) return false;
        for (var i = 0; i < count; i++)
        {
            var a = Attributes![i]; var b = other.Attributes![i];
            if (a.Location != b.Location || a.Components != b.Components || a.Type != b.Type ||
                a.Offset != b.Offset || a.Normalized != b.Normalized) return false;
        }
        return true;
    }
    public override readonly bool Equals(object? obj) => obj is VertexFormatDescriptor other && Equals(other);
    public override readonly int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Stride);
        if (Attributes != null)
            foreach (var a in Attributes)
            {
                hash.Add(a.Location); hash.Add(a.Components); hash.Add(a.Type);
                hash.Add(a.Offset); hash.Add(a.Normalized);
            }
        return hash.ToHashCode();
    }
}

public enum BufferUsage
{
    Static,     // Data set once
    Dynamic,    // Data updated frequently
    Stream      // Data updated every frame
}

public enum MeshIndexFormat : byte
{
    UInt16,
    UInt32
}

public class GraphicsDriverConfig
{
    public required IPlatform Platform { get; set; }
    public bool VSync { get; set; } = true;
    public int MaxGlobalSnapshots { get; set; } = GraphicsConfig.DefaultMaxGlobalSnapshots;
    public int MaxMeshes { get; set; } = GraphicsConfig.DefaultMaxMeshes;
}

public interface IGraphicsDriver
{
    string ShaderExtension { get; }

    void Init(GraphicsDriverConfig config);
    void Shutdown();

    bool BeginFrame();
    void EndFrame();

    void SetViewport(in RectInt viewport);
    void SetScissor(in RectInt scissor);
    void ClearScissor();

    nuint CreateMesh<T>(int maxVertices, int maxIndices, BufferUsage usage, string name = "", MeshIndexFormat indexFormat = MeshIndexFormat.UInt16) where T : IVertex;
    void DestroyMesh(nuint handle);
    void BindMesh(nuint handle);
    void UpdateMesh(nuint handle, ReadOnlySpan<byte> vertexData, ReadOnlySpan<ushort> indexData);
    void UpdateMesh(nuint handle, ReadOnlySpan<byte> vertexData, ReadOnlySpan<uint> indexData);

    nuint CreateUniformBuffer(int sizeInBytes, BufferUsage usage, string name = "");
    void DestroyBuffer(nuint handle);
    void UpdateUniformBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data);
    void BindUniformBuffer(nuint buffer, int slot);

    nuint CreateTexture(int width, int height, ReadOnlySpan<byte> data, TextureFormat format = TextureFormat.RGBA8, TextureFilter filter = TextureFilter.Linear, string? name=null);
    void UpdateTexture(nuint handle, in Vector2Int size, ReadOnlySpan<byte> data);
    void UpdateTextureRegion(nuint handle, in RectInt region, ReadOnlySpan<byte> data, int srcWidth = -1);
    void DestroyTexture(nuint handle);
    void BindTexture(nuint handle, int slot, TextureFilter filter = TextureFilter.Point);

    nuint CreateTextureArray(int width, int height, int layers);
    nuint CreateTextureArray(int width, int height, byte[][] layerData, TextureFormat format, TextureFilter filter, string? name=null);
    void UpdateTextureLayer(nuint handle, int layer, ReadOnlySpan<byte> data);

    nuint CreateShader(string name, string vertexSource, string fragmentSource, List<ShaderBinding> bindings, ShaderFlags flags = ShaderFlags.None);
    void DestroyShader(nuint handle);
    void BindShader(nuint handle);

    void SetBlendMode(BlendMode mode);
    void SetTextureFilter(TextureFilter filter);
    void SetUniform(string name, ReadOnlySpan<byte> data);

    void SetGlobalsCount(int count);
    void SetGlobals(int index, ReadOnlySpan<byte> data);
    void BindGlobals(int index);

    void DrawElements(int firstIndex, int indexCount, int baseVertex = 0);
    /// <summary>Whether this driver supports slot-1 instance streams and indexed instancing.</summary>
    bool SupportsInstancing => false;
    /// <summary>Upload a four-byte-aligned range into a vertex-only mesh (zero indices).</summary>
    void UpdateInstanceData(nuint stream, int byteOffset, ReadOnlySpan<byte> data) =>
        throw new NotSupportedException("This graphics driver does not support instancing.");
    /// <summary>Bind slot 1, or disable instancing with zero. Zero remains valid on older drivers.</summary>
    void BindInstanceStream(nuint stream)
    {
        if (stream != 0) throw new NotSupportedException("This graphics driver does not support instancing.");
    }
    void DrawElementsInstanced(int firstIndex, int indexCount, int instanceCount, int firstInstance) =>
        throw new NotSupportedException("This graphics driver does not support instancing.");

    void SetVSync(bool vsync) { }

    nuint CreateFence();
    void WaitFence(nuint fence);
    void DeleteFence(nuint fence);

    void BeginScenePass(Color clearColor);
    void ResumeScenePass();
    void EndScenePass();

    // Render Texture support (BGRA8 default matches swap chain format for pipeline compatibility)
    nuint CreateRenderTexture(int width, int height, TextureFormat format = TextureFormat.BGRA8, int sampleCount = 1, string? name = null, bool depth = false);
    // Borrowed single-sample depth; MSAA resolves the nearest (minimum 0..1)
    // sample at EndRenderTexturePass. Zero when depth sampling is unsupported.
    nuint GetRenderTextureDepthTexture(nuint renderTexture) => nuint.Zero;
    void DestroyRenderTexture(nuint handle);
    void BeginRenderTexturePass(nuint renderTexture, Color clearColor);
    void ResumeRenderTexturePass(nuint renderTexture);
    void EndRenderTexturePass();
    Task<byte[]> ReadRenderTexturePixelsAsync(nuint renderTexture);
    Task<Color> ReadPixelAsync(nuint renderTexture, int x, int y);
}
