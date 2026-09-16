//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public class RenderTexture : ITexture, IDisposable
{
    public nuint Handle { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int SampleCount { get; private set; }
    public TextureFormat Format { get; private set; }
    public bool HasDepth { get; private set; }
    /// <summary>
    /// Borrowed handle for sampling this render target's depth attachment.
    /// MSAA targets expose the nearest-sample depth resolved at pass end.
    /// Zero when depth is disabled/unsupported. The render texture owns it.
    /// </summary>
    public nuint DepthTextureHandle { get; private set; }

    float IImage.ImageWidth => Width;
    float IImage.ImageHeight => Height;
    TextureFilter ITexture.Filter => TextureFilter.Linear;

    internal RenderTexture(
        nuint handle,
        int width,
        int height,
        int sampleCount = 1,
        TextureFormat format = TextureFormat.BGRA8,
        bool depth = false)
    {
        Handle = handle;
        Width = width;
        Height = height;
        SampleCount = sampleCount;
        Format = format;
        HasDepth = depth;
        DepthTextureHandle = depth
            ? Graphics.Driver.GetRenderTextureDepthTexture(handle)
            : nuint.Zero;
    }

    public static RenderTexture Create(
        int width,
        int height,
        int sampleCount = 1,
        TextureFormat format = TextureFormat.BGRA8,
        string? name = null,
        bool depth = false)
    {
        var handle = Graphics.Driver.CreateRenderTexture(width, height, format: format, sampleCount: sampleCount, name: name, depth: depth);
        return new RenderTexture(handle, width, height, sampleCount, format, depth);
    }

    public void Dispose()
    {
        if (Handle == 0) return;

        Graphics.Driver.DestroyRenderTexture(Handle);
        Handle = 0;
        DepthTextureHandle = 0;
    }
}
