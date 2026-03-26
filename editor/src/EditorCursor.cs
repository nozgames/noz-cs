//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static class EditorCursor
{
    public static void SetArrow() => Cursor.Set(EditorAssets.Sprites.CursorArrow);
    public static void SetMove() => Cursor.Set(EditorAssets.Sprites.CursorMove);
    public static void SetScale() => Cursor.Set(EditorAssets.Sprites.CursorScale);
    public static void SetScale(float rotation) => Cursor.Set(EditorAssets.Sprites.CursorScale, rotation);

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
    public static void SetRotate() => Cursor.Set(EditorAssets.Sprites.CursorRotate);
    public static void SetRotate(float rotation) => Cursor.Set(EditorAssets.Sprites.CursorRotate, rotation);

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
    public static void SetCrosshair() => Cursor.SetCrosshair();
}
