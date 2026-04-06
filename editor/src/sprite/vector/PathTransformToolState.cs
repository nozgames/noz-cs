//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal struct PathTransformToolState
{
    public SpriteDocument Document;
    public SpriteEditor Editor;
    public (SpritePath Path, Vector2 SavedTranslation, float SavedRotation, Vector2 SavedScale)[] Snapshots;
    public Vector2 Centroid;

    public static PathTransformToolState? Create(SpriteDocument document, SpriteEditor editor, List<SpritePath> selectedPaths)
    {
        if (selectedPaths.Count == 0) return null;

        var snapshots = new (SpritePath, Vector2, float, Vector2)[selectedPaths.Count];
        var sum = Vector2.Zero;
        var count = 0;

        for (var i = 0; i < selectedPaths.Count; i++)
        {
            var path = selectedPaths[i];
            snapshots[i] = (path, path.PathTranslation, path.PathRotation, path.PathScale);

            path.UpdateBounds();
            var center = path.Bounds.Center;
            sum += center;
            count++;
        }

        if (count == 0) return null;

        return new PathTransformToolState
        {
            Document = document,
            Editor = editor,
            Snapshots = snapshots,
            Centroid = sum / count,
        };
    }

    public static Vector2 ComputeWorldCenter(
        Vector2 localBoundsCenter, Vector2 translation, float rotation, Vector2 scale)
    {
        var c = localBoundsCenter;
        var xform = Matrix3x2.CreateTranslation(-c)
            * Matrix3x2.CreateScale(scale)
            * Matrix3x2.CreateRotation(rotation)
            * Matrix3x2.CreateTranslation(c)
            * Matrix3x2.CreateTranslation(translation);
        return Vector2.Transform(c, xform);
    }

    public void UpdatePaths()
    {
        foreach (var (path, _, _, _) in Snapshots)
        {
            path.MarkDirty();
            path.UpdateSamples();
            path.UpdateBounds();
        }
        ((VectorSpriteEditor)Editor).MarkDirty();
    }
}
