//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

/// <summary>Mesh submission using the caller's current shader, textures, blend mode and layer.</summary>
public static class Graphics3D
{
    /// <summary>
    /// Draws an untransformed mesh with world-space normals.
    /// The caller owns the render pass and must restore the projection before drawing 2D UI.
    /// This deliberately does not choose materials, lighting or render targets.
    /// </summary>
    public static void Draw(Mesh mesh, in Matrix4x4 objectToClip, ushort order = 0)
        => Draw(mesh, Matrix4x4.Identity, objectToClip, order);

    /// <summary>Draws a transformed mesh with a per-draw inverse-transpose normal matrix.</summary>
    public static void Draw(Mesh mesh, in Matrix4x4 model, in Matrix4x4 viewProjection, ushort order = 0)
    {
        if (mesh.RenderMesh.Handle == nuint.Zero || mesh.Indices.Length == 0)
            return;

        SetTransform(model, viewProjection);
        Graphics.SetMesh(mesh.RenderMesh);
        Graphics.DrawElements(mesh.Indices.Length, order: order);
    }

    /// <summary>
    /// Sets transforms for mesh or raw indexed draws. Shaders append a mat4x4
    /// normal_to_world after projection/time in globals (byte offset 80), then
    /// model (byte offset 144) for world-space material sampling. Shaders that
    /// only use the normal matrix retain the same prefix layout.
    /// </summary>
    public static void SetTransform(in Matrix4x4 model, in Matrix4x4 viewProjection)
    {
        if (!Matrix4x4.Invert(model, out var inverseModel))
            throw new ArgumentException("The model transform must be invertible.", nameof(model));

        // System.Numerics uses row vectors. Raw matrix bytes are read as columns
        // by WGSL, so no additional transpose is needed during upload.
        var normalToWorld = Matrix4x4.Transpose(inverseModel);
        Graphics.SetDrawParameters(new MeshDraw(normalToWorld, model));
        Graphics.SetViewProjection(model * viewProjection);
    }

    private readonly record struct MeshDraw(Matrix4x4 NormalToWorld, Matrix4x4 Model);
}
