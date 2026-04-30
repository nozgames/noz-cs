//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public enum SystemCursor
{
    None,
    Default,
    Move,
    Crosshair,
    Wait,
    ResizeEW,
    ResizeNS,
    ResizeNWSE,
    ResizeNESW,
    Text,
}

public readonly struct SpriteCursor(Sprite sprite, float scale = 1.0f, float rotation = 0, Vector2 hotspot = default)
{
    public readonly Sprite Sprite = sprite;
    public readonly float Scale = scale;
    public readonly float Rotation = rotation;
    public readonly Vector2 Hotspot = hotspot;
}

public static class Cursor
{
    private const float Scale = 0.75f;

    private static SpriteCursor _spriteCursor;
    private static SystemCursor _systemCursor = SystemCursor.Default;
    private static readonly Camera _camera = new() { FlipY = false };

    public static Shader? Shader { get; set; }

    public static Vector2? PositionOverride { get; set; }

    public static bool Enabled { get; set; } = true;

    internal static SpriteCursor ActiveSpriteCursor => _spriteCursor;
    internal static SystemCursor ActiveSystemCursor => _systemCursor;

    public static void Set(SystemCursor cursor)
    {
        if (!Enabled)
        {
            _spriteCursor = default;
            _systemCursor = SystemCursor.None;
            Application.Platform.SetCursor(SystemCursor.None);
            return;
        }

        _spriteCursor = default;
        _systemCursor = cursor;
        Application.Platform.SetCursor(cursor);
    }

    public static void Set(SpriteCursor cursor)
    {
        if (!Enabled)
        {
            _spriteCursor = default;
            _systemCursor = SystemCursor.None;
            Application.Platform.SetCursor(SystemCursor.None);
            return;
        }

        _spriteCursor = cursor;
        _systemCursor = SystemCursor.None;
        Application.Platform.SetCursor(SystemCursor.None);
    }

    public static void Set(Sprite sprite) => Set(new SpriteCursor(sprite));
    public static void Set(Sprite sprite, float rotation) => Set(new SpriteCursor(sprite, rotation: rotation));

    public static void SetDefault() => Set(SystemCursor.Default);
    public static void SetCrosshair() => Set(SystemCursor.Crosshair);
    public static void SetMove() => Set(SystemCursor.Move);
    public static void SetText() => Set(SystemCursor.Text);
    public static void SetWait() => Set(SystemCursor.Wait);
    public static void Hide() => Set(SystemCursor.None);

    internal static void Update()
    {
        if (!Enabled) return;
        if (Shader == null)
            return;

        var sprite = _spriteCursor.Sprite;
        if (sprite == null || !Input.MouseInWindow) return;

        _camera.SetExtents(new Rect(0, 0, Graphics.RenderSize.X, Graphics.RenderSize.Y));
        _camera.Update();

        var position = PositionOverride ?? Input.MousePosition;
        position -= _spriteCursor.Hotspot * Scale * _spriteCursor.Scale;
        PositionOverride = null;

        using var _ = Graphics.PushState();
        Graphics.SetCamera(_camera);
        Graphics.SetShader(Shader);
        Graphics.SetLayer(Graphics.MaxLayer);
        Graphics.SetBlendMode(BlendMode.Alpha);
        Graphics.SetColor(Color.White);
        Graphics.SetTransform(
            Matrix3x2.CreateScale(sprite.PixelsPerUnit * Scale * _spriteCursor.Scale) *
            Matrix3x2.CreateRotation(_spriteCursor.Rotation) *
            Matrix3x2.CreateTranslation(position));
        Graphics.DrawFlat(sprite);
    }
}
