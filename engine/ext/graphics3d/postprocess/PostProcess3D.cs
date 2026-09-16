//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace NoZ;

/// <summary>
/// Optional depth-aware effects built on NoZ's existing post-process pipeline.
/// </summary>
public static class PostProcess3D
{
    public const string SsaoShaderName = "ssao";
    public const string SsaoBlurShaderName = "ssao_blur";
    public const string SsaoCompositeShaderName = "ssao_composite";

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SsaoParams(
        Matrix4x4 inverseProjection,
        Vector4 projectionScale,
        Vector4 tuning)
    {
        public readonly Matrix4x4 InverseProjection = inverseProjection;
        public readonly Vector4 ProjectionScale = projectionScale;
        public readonly Vector4 Tuning = tuning;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SsaoBlurParams(
        Matrix4x4 inverseProjection,
        Vector4 tuning)
    {
        public readonly Matrix4x4 InverseProjection = inverseProjection;
        public readonly Vector4 Tuning = tuning;
    }

    /// <summary>
    /// Applies SSAO to the current post-process color using the scene's depth.
    /// Update the camera for the scene size first. Returns false without changing
    /// the pipeline when disabled, no sampleable depth exists,
    /// or the extension's shaders are unavailable. Call before drawing UI.
    /// The deferred renderer currently shares named uniforms, so use one SSAO
    /// view per frame; different views require per-draw uniform snapshots first.
    /// </summary>
    public static bool Ssao(Camera3D camera, SsaoSettings settings)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(settings);
        var depthTexture = PostProcess.SceneDepthTextureHandle;
        // Capture the stage's input, not the original scene: earlier effects
        // must survive the AO/blur passes that replace the pipeline's current target.
        var inputColorTexture = PostProcess.CurrentColorTextureHandle;
        if (!settings.Enabled || depthTexture == nuint.Zero || inputColorTexture == nuint.Zero ||
            camera.ScreenSize.X < 1 || camera.ScreenSize.Y < 1)
            return false;

        if (!Matrix4x4.Invert(camera.ProjectionMatrix, out var inverseProjection))
            return false;

        // Resolve through the asset registry rather than game-generated fields.
        // The host's normal manifest load/reload/unload owns these shader assets.
        // No separate shader cache or lifetime is introduced here.
        var aoShader = Asset.Load(AssetType.Shader, SsaoShaderName) as Shader;
        var blurShader = Asset.Load(AssetType.Shader, SsaoBlurShaderName) as Shader;
        var compositeShader = Asset.Load(AssetType.Shader, SsaoCompositeShaderName) as Shader;
        if (aoShader == null || blurShader == null || compositeShader == null)
            return false;

        var parameters = new SsaoParams(
            // Uniform matrices are copied verbatim. WGSL interprets the
            // row-major System.Numerics memory as column-major, which provides
            // the transpose needed to convert the CPU row-vector inverse into
            // the GPU column-vector inverse. Transposing here a second time
            // breaks view-position reconstruction.
            inverseProjection,
            new Vector4(
                camera.ProjectionMatrix.M11 * 0.5f,
                camera.ProjectionMatrix.M22 * 0.5f,
                MathF.Max(settings.SurfaceThickness, 0f),
                MathF.Max(settings.NoiseFloor, 0f)),
            new Vector4(
                MathF.Max(settings.Radius, 0.001f),
                MathF.Max(settings.Strength, 0f),
                Math.Clamp(settings.Bias, 0f, 1f),
                Math.Clamp(settings.MaxDarkening, 0f, 1f)));

        var scale = Math.Clamp(settings.ResolutionScale, 0.25f, 1f);
        var aoWidth = Math.Max(1, (int)MathF.Ceiling(camera.ScreenSize.X * scale));
        var aoHeight = Math.Max(1, (int)MathF.Ceiling(camera.ScreenSize.Y * scale));

        PostProcess.BeginBlit(aoShader, aoWidth, aoHeight);
        Graphics.SetTexture(depthTexture, slot: 1);
        Graphics.SetTextureFilter(TextureFilter.Point, slot: 1);
        Graphics.SetUniform("ssao_params", parameters);
        PostProcess.EndBlit();

        Blur(blurShader, depthTexture, inverseProjection, camera, settings, Vector2.UnitX, aoWidth, aoHeight);
        Blur(blurShader, depthTexture, inverseProjection, camera, settings, Vector2.UnitY, aoWidth, aoHeight);

        PostProcess.BeginBlit(
            compositeShader,
            camera.ScreenSize.X,
            camera.ScreenSize.Y);
        Graphics.SetTexture(inputColorTexture, slot: 1);
        Graphics.SetTextureFilter(TextureFilter.Linear, slot: 1);
        Graphics.SetColor(new Color(settings.DebugView ? 1f : 0f, 0f, 0f, 1f));
        PostProcess.EndBlit();
        return true;
    }

    private static void Blur(
        Shader shader,
        nuint depthTexture,
        in Matrix4x4 inverseProjection,
        Camera3D camera,
        SsaoSettings settings,
        Vector2 direction,
        int width,
        int height)
    {
        var parameters = new SsaoBlurParams(
            inverseProjection,
            new Vector4(
                MathF.Max(settings.Radius, 0.001f),
                camera.ProjectionMatrix.M11 * 0.5f,
                camera.ProjectionMatrix.M22 * 0.5f,
                0f));

        PostProcess.BeginBlit(shader, width, height);
        Graphics.SetTexture(depthTexture, slot: 1);
        Graphics.SetTextureFilter(TextureFilter.Point, slot: 1);
        Graphics.SetUniform("ssao_blur_params", parameters);
        // Per-draw uniforms are not snapshotted by NoZ's deferred renderer;
        // vertex color is, so carry the blur direction on the blit quad.
        Graphics.SetColor(new Color(direction.X, direction.Y, 0f, 1f));
        PostProcess.EndBlit();
    }
}
