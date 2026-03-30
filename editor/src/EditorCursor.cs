//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static class EditorCursor
{
    private static SpriteCursor? _cursor;
    private static SystemCursor _systemCursor;

    public static void Init()
    {
        Cursor.Shader = EditorAssets.Shaders.Sprite;
    }

    public static void SetArrow() => SetSprite(new SpriteCursor(EditorAssets.Sprites.CursorArrow));
    public static void SetMove() => SetSprite(new SpriteCursor(EditorAssets.Sprites.CursorMove));
    public static void SetScale() => SetSprite(new SpriteCursor(EditorAssets.Sprites.CursorScale));
    public static void SetScale(float rotation) => SetSprite(new SpriteCursor(EditorAssets.Sprites.CursorScale, rotation));

    public static void SetScale(SpritePathHandle handle, float selectionRotation)
    {
        var rotation = handle switch
        {
            SpritePathHandle.ScaleTop or SpritePathHandle.ScaleBottom => selectionRotation + MathF.PI / 2f,
            SpritePathHandle.ScaleLeft or SpritePathHandle.ScaleRight => selectionRotation,
            SpritePathHandle.ScaleTopLeft or SpritePathHandle.ScaleBottomRight => selectionRotation + MathF.PI / 4f,
            SpritePathHandle.ScaleTopRight or SpritePathHandle.ScaleBottomLeft => selectionRotation - MathF.PI / 4f,
            _ => selectionRotation,
        };
        SetScale(rotation);
    }

    public static void SetRotate() => SetSprite(new SpriteCursor(EditorAssets.Sprites.CursorRotate));
    public static void SetRotate(float rotation) => SetSprite(new SpriteCursor(EditorAssets.Sprites.CursorRotate, rotation));

    public static void SetRotate(SpritePathHandle handle, float selectionRotation)
    {
        var rotation = handle switch
        {
            SpritePathHandle.RotateBottomRight => selectionRotation,
            SpritePathHandle.RotateBottomLeft => selectionRotation + MathF.PI / 2f,
            SpritePathHandle.RotateTopLeft => selectionRotation + MathF.PI,
            SpritePathHandle.RotateTopRight => selectionRotation - MathF.PI / 2f,
            _ => selectionRotation,
        };
        SetRotate(rotation);
    }

    public static void SetDropper() => SetSprite(new SpriteCursor(EditorAssets.Sprites.CurorDropper));

    public static void SetCrosshair()
    {
        _cursor = null;
        _systemCursor = SystemCursor.Crosshair;
    }

    public static void Begin()
    {
        if (_cursor.HasValue)
            ElementTree.BeginCursor(_cursor.Value);
        else
            ElementTree.BeginCursor(_systemCursor);
    }

    public static void End()
    {
        ElementTree.EndCursor();
        SetArrow();
    }

    private static void SetSprite(SpriteCursor cursor)
    {
        _cursor = cursor;
        _systemCursor = SystemCursor.None;
    }
}
