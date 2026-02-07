//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct RenderTexture : IDisposable
{
    public nuint Handle { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int SampleCount { get; private set; }
    public readonly bool IsValid => Handle != 0;

    internal RenderTexture(nuint handle, int width, int height, int sampleCount = 1)
    {
        Handle = handle;
        Width = width;
        Height = height;
        SampleCount = sampleCount;
    }

    public static RenderTexture Create(int width, int height, int sampleCount = 1, string? name = null)
    {
        var handle = Graphics.Driver.CreateRenderTexture(width, height, sampleCount: sampleCount, name: name);
        return new RenderTexture(handle, width, height, sampleCount);
    }

    public void Dispose()
    {
        if (Handle == 0) return;

        Graphics.Driver.DestroyRenderTexture(Handle);
        Handle = 0;
    }
}
