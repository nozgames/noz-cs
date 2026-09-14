//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using NoZ;

namespace NoZ;

/// <summary>
/// A world-space ray with a normalized direction.
/// </summary>
public readonly record struct Ray3D(Vector3 Origin, Vector3 Direction);

/// <summary>
/// An optional perspective camera. NoZ's core only receives the
/// resulting view-projection matrix through its generic graphics API.
/// </summary>
public sealed class Camera3D
{
    private Vector2Int _screenSize;

    public Vector3 Position { get; set; } = new(0f, 0f, 5f);
    public Vector3 Target { get; set; } = Vector3.Zero;
    public Vector3 Up { get; set; } = Vector3.UnitY;
    public float FieldOfView { get; set; } = MathF.PI / 4f;
    // Keep this range reasonably tight to preserve depth precision.
    public float NearClip { get; set; } = 0.1f;
    public float FarClip { get; set; } = 256f;
    public Vector2 ProjectionOffset { get; set; }

    public Vector2Int ScreenSize => _screenSize;
    public float AspectRatio { get; private set; } = 1f;
    public Matrix4x4 ViewMatrix { get; private set; } = Matrix4x4.Identity;
    public Matrix4x4 ProjectionMatrix { get; private set; } = Matrix4x4.Identity;
    public Matrix4x4 ViewProjectionMatrix { get; private set; } = Matrix4x4.Identity;

    public Vector3 Forward
    {
        get
        {
            var forward = Target - Position;
            return forward.LengthSquared() > float.Epsilon
                ? Vector3.Normalize(forward)
                : -Vector3.UnitZ;
        }
    }

    public void Update(Vector2Int screenSize)
    {
        var width = Math.Max(screenSize.X, 1);
        var height = Math.Max(screenSize.Y, 1);
        _screenSize = new Vector2Int(width, height);
        AspectRatio = (float)width / height;

        var forward = Forward;
        var up = Up.LengthSquared() > float.Epsilon ? Vector3.Normalize(Up) : Vector3.UnitY;
        if (Vector3.Cross(forward, up).LengthSquared() < 0.000001f)
            up = MathF.Abs(forward.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitZ;

        var nearClip = MathF.Max(NearClip, 0.0001f);
        var farClip = MathF.Max(FarClip, nearClip + 0.0001f);
        var fieldOfView = Math.Clamp(FieldOfView, 0.01f, MathF.PI - 0.01f);

        ViewMatrix = Matrix4x4.CreateLookAt(Position, Position + forward, up);
        ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            fieldOfView,
            AspectRatio,
            nearClip,
            farClip);
        ViewProjectionMatrix = ViewMatrix * ProjectionMatrix *
                               Matrix4x4.CreateTranslation(ProjectionOffset.X, ProjectionOffset.Y, 0f);
    }

    /// <summary>
    /// Creates a world-space ray through a render-target pixel. The camera must
    /// have been updated for the current render size first.
    /// </summary>
    public Ray3D ScreenPointToRay(Vector2 screenPosition)
    {
        var width = Math.Max(_screenSize.X, 1);
        var height = Math.Max(_screenSize.Y, 1);
        var ndc = new Vector2(
            screenPosition.X / width * 2f - 1f,
            1f - screenPosition.Y / height * 2f);

        if (!Matrix4x4.Invert(ViewProjectionMatrix, out var inverse))
            return new Ray3D(Position, Forward);

        var near = Unproject(new Vector4(ndc, 0f, 1f), inverse);
        var far = Unproject(new Vector4(ndc, 1f, 1f), inverse);
        var direction = far - near;
        if (direction.LengthSquared() < 0.000001f)
            direction = Forward;
        else
            direction = Vector3.Normalize(direction);

        return new Ray3D(Position, direction);
    }

    private static Vector3 Unproject(Vector4 point, Matrix4x4 inverse)
    {
        point = Vector4.Transform(point, inverse);
        if (MathF.Abs(point.W) > 0.000001f)
            point /= point.W;
        return new Vector3(point.X, point.Y, point.Z);
    }
}
