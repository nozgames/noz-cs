//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;

namespace NoZ;

internal static class RenderTexturePool
{
    private struct PooledTexture
    {
        public nuint Handle;
        public int Width;
        public int Height;
        public int SampleCount;
        public int Frame;
    }

    private const int MaxPooledTextures = 16;
    private static readonly PooledTexture[] _pool = new PooledTexture[MaxPooledTextures];
    private static int _frame = 1;

    public static RenderTexture Acquire(int width, int height, int sampleCount = 1)
    {
        // Use existing
        for (int i = 0; i < MaxPooledTextures; i++)
        {
            ref var entry = ref _pool[i];
            if (entry.Handle != nuint.Zero &&
                entry.Frame != _frame &&
                entry.Width == width &&
                entry.Height == height &&
                entry.SampleCount == sampleCount)
            {
                entry.Frame = _frame;
                return new RenderTexture(entry.Handle, width, height, sampleCount);
            }
        }

        for (int i = 0; i < MaxPooledTextures; i++)
        {
            ref var entry = ref _pool[i];
            if (entry.Handle == 0)
            {
                entry.Frame = _frame;
                entry.Width = width;
                entry.Height = height;
                entry.SampleCount = sampleCount;
                entry.Handle = Graphics.Driver.CreateRenderTexture(width, height, sampleCount: sampleCount, name: "PooledRT");
                return new RenderTexture(entry.Handle, width, height, sampleCount);
            }
        }

        Debug.Assert(false, "RenderTexturePool exhausted");

        return default;
    }

    internal static void FlushPendingReleases()
    {
        for (int i=0; i<MaxPooledTextures; i++)
        {
            ref var entry = ref _pool[i];
            if (entry.Handle == nuint.Zero) continue;
            if (int.Abs(entry.Frame - _frame) < 2) continue;

            Graphics.Driver.DestroyRenderTexture(entry.Handle);
            entry = default;
        }

        _frame++;
    }

    public static void Clear()
    {
        for (int i = 0; i < MaxPooledTextures; i++)
        {
            ref var entry = ref _pool[i];
            if (entry.Handle == 0) continue;
            Graphics.Driver.DestroyRenderTexture(_pool[i].Handle);
            entry = default;
        }
    }
}
