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
    public TextureFormat Format { get; private set; }
    public readonly bool IsValid => Handle != 0;

    internal RenderTexture(nuint handle, int width, int height, int sampleCount = 1, TextureFormat format = TextureFormat.BGRA8)
    {
        Handle = handle;
        Width = width;
        Height = height;
        SampleCount = sampleCount;
        Format = format;
    }

    public static RenderTexture Create(int width, int height, int sampleCount = 1, TextureFormat format = TextureFormat.BGRA8, string? name = null)
    {
        var handle = Graphics.Driver.CreateRenderTexture(width, height, format: format, sampleCount: sampleCount, name: name);
        return new RenderTexture(handle, width, height, sampleCount, format);
    }

    public void Dispose()
    {
        if (Handle == 0) return;

        Graphics.Driver.DestroyRenderTexture(Handle);
        Handle = 0;
    }
}
