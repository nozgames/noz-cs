//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ;
using NoZ.Platform;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    // Mesh Management (to be implemented in Phase 2)
    public nuint CreateMesh<T>(int maxVertices, int maxIndices, BufferUsage usage, string name = "") where T : IVertex
    {
        throw new NotImplementedException("CreateMesh will be implemented in Phase 2");
    }

    public void DestroyMesh(nuint handle)
    {
        throw new NotImplementedException("DestroyMesh will be implemented in Phase 2");
    }

    public void BindMesh(nuint handle)
    {
        throw new NotImplementedException("BindMesh will be implemented in Phase 2");
    }

    public void UpdateMesh(nuint handle, ReadOnlySpan<byte> vertexData, ReadOnlySpan<ushort> indexData)
    {
        throw new NotImplementedException("UpdateMesh will be implemented in Phase 2");
    }

    // Buffer Management (to be implemented in Phase 2)
    public nuint CreateUniformBuffer(int sizeInBytes, BufferUsage usage, string name = "")
    {
        throw new NotImplementedException("CreateUniformBuffer will be implemented in Phase 2");
    }

    public void DestroyBuffer(nuint handle)
    {
        throw new NotImplementedException("DestroyBuffer will be implemented in Phase 2");
    }

    public void UpdateUniformBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data)
    {
        throw new NotImplementedException("UpdateUniformBuffer will be implemented in Phase 2");
    }

    public void BindUniformBuffer(nuint buffer, int slot)
    {
        throw new NotImplementedException("BindUniformBuffer will be implemented in Phase 2");
    }

    // Texture Management (to be implemented in Phase 2)
    public nuint CreateTexture(int width, int height, ReadOnlySpan<byte> data, TextureFormat format = TextureFormat.RGBA8, TextureFilter filter = TextureFilter.Linear, string? name = null)
    {
        throw new NotImplementedException("CreateTexture will be implemented in Phase 2");
    }

    public void UpdateTexture(nuint handle, int width, int height, ReadOnlySpan<byte> data)
    {
        throw new NotImplementedException("UpdateTexture will be implemented in Phase 2");
    }

    public void DestroyTexture(nuint handle)
    {
        throw new NotImplementedException("DestroyTexture will be implemented in Phase 2");
    }

    public void BindTexture(nuint handle, int slot)
    {
        throw new NotImplementedException("BindTexture will be implemented in Phase 2");
    }

    // Texture Array Management (to be implemented in Phase 2)
    public nuint CreateTextureArray(int width, int height, int layers)
    {
        throw new NotImplementedException("CreateTextureArray will be implemented in Phase 2");
    }

    public nuint CreateTextureArray(int width, int height, byte[][] layerData, TextureFormat format, TextureFilter filter, string? name = null)
    {
        throw new NotImplementedException("CreateTextureArray will be implemented in Phase 2");
    }

    public void UpdateTextureLayer(nuint handle, int layer, ReadOnlySpan<byte> data)
    {
        throw new NotImplementedException("UpdateTextureLayer will be implemented in Phase 2");
    }

    // Shader Management (to be implemented in Phase 3)
    public nuint CreateShader(string name, string vertexSource, string fragmentSource)
    {
        throw new NotImplementedException("CreateShader will be implemented in Phase 3");
    }

    public void DestroyShader(nuint handle)
    {
        throw new NotImplementedException("DestroyShader will be implemented in Phase 3");
    }

    public void BindShader(nuint handle)
    {
        throw new NotImplementedException("BindShader will be implemented in Phase 3");
    }

    // Blend Mode (to be implemented in Phase 4)
    public void SetBlendMode(BlendMode mode)
    {
        throw new NotImplementedException("SetBlendMode will be implemented in Phase 4");
    }

    // Drawing (to be implemented in Phase 4)
    public void DrawElements(int firstIndex, int indexCount, int baseVertex = 0)
    {
        throw new NotImplementedException("DrawElements will be implemented in Phase 4");
    }

    // Synchronization (to be implemented in Phase 6)
    public nuint CreateFence()
    {
        throw new NotImplementedException("CreateFence will be implemented in Phase 6");
    }

    public void WaitFence(nuint fence)
    {
        throw new NotImplementedException("WaitFence will be implemented in Phase 6");
    }

    public void DeleteFence(nuint fence)
    {
        throw new NotImplementedException("DeleteFence will be implemented in Phase 6");
    }

    // Offscreen Rendering (to be implemented in Phase 5)
    public void ResizeOffscreenTarget(int width, int height, int msaaSamples)
    {
        throw new NotImplementedException("ResizeOffscreenTarget will be implemented in Phase 5");
    }

    public void BeginScenePass(Color clearColor)
    {
        throw new NotImplementedException("BeginScenePass will be implemented in Phase 5");
    }

    public void EndScenePass()
    {
        throw new NotImplementedException("EndScenePass will be implemented in Phase 5");
    }

    public void Composite(nuint compositeShader)
    {
        throw new NotImplementedException("Composite will be implemented in Phase 5");
    }
}
