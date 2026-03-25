//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal struct PathTransformToolState
{
    public SpriteDocument Document;
    public (SpritePath Path, Vector2 SavedTranslation, float SavedRotation, Vector2 SavedScale)[] Snapshots;
    public Vector2 Centroid;

    public static PathTransformToolState? Create(SpriteDocument document, List<SpritePath> selectedPaths)
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
            Snapshots = snapshots,
            Centroid = sum / count,
        };
    }

    public void UpdatePaths()
    {
        foreach (var (path, _, _, _) in Snapshots)
        {
            path.MarkDirty();
            path.UpdateSamples();
            path.UpdateBounds();
        }
        Document.IncrementVersion();
    }

    public void Commit()
    {
        UpdatePaths();
        Document.UpdateBounds();
    }
}
