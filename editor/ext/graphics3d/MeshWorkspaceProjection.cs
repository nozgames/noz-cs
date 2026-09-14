//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using NoZ;

namespace NoZ.Editor.Graphics3D;

public static class MeshWorkspaceProjection
{
    public static Matrix4x4 Create(Camera3D camera, in Matrix3x2 workspaceView, Rect worldBounds)
    {
        // Match the square thumbnail's perspective. Pan, zoom, and viewport
        // aspect are supplied by the workspace after projecting the model.
        camera.Update(new Vector2Int(1, 1));

        // Map perspective NDC into the document's workspace rectangle. The
        // workspace uses downward Y, while the perspective camera uses upward Y.
        var clipToDocument = Matrix4x4.CreateScale(worldBounds.Width * 0.5f, -worldBounds.Height * 0.5f, 1f) *
                             Matrix4x4.CreateTranslation(worldBounds.Center.X, worldBounds.Center.Y, 0f);

        // Match Graphics.SetCamera's workspace-to-clip Y flip, preserving Z/W
        // for depth testing. Cursor-anchored zoom now moves every projected point
        // exactly like a 2D asset at the same workspace position.
        return camera.ViewProjectionMatrix * clipToDocument *
               new Matrix4x4(workspaceView) * Matrix4x4.CreateScale(1f, -1f, 1f);
    }
}
