//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal struct PathToolState
{
    public SpriteDocument Document;
    public (SpritePath Path, SpritePathAnchor[] Saved)[] Snapshots;
    public Vector2 Centroid;

    public static PathToolState? Create(SpriteDocument document)
    {
        var paths = new List<SpritePath>();
        document.RootLayer.CollectPathsWithSelection(paths);
        if (paths.Count == 0) return null;

        var snapshots = new (SpritePath, SpritePathAnchor[])[paths.Count];
        var sum = Vector2.Zero;
        var count = 0;

        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            snapshots[i] = (path, path.SnapshotAnchors());

            for (var j = 0; j < path.Anchors.Count; j++)
            {
                if (!path.Anchors[j].IsSelected) continue;
                sum += path.Anchors[j].Position;
                count++;
            }
        }

        if (count == 0) return null;

        return new PathToolState
        {
            Document = document,
            Snapshots = snapshots,
            Centroid = sum / count,
        };
    }

    public void UpdatePaths()
    {
        var snap = Input.IsCtrlDown(InputScope.All);
        foreach (var (path, _) in Snapshots)
        {
            if (snap) path.SnapSelectedToPixelGrid();
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
