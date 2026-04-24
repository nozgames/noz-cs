//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class AnchorMode : AnchorBasedMode
{
    // Drag state for anchor move
    private SpritePath[]? _movePaths;
    private SpritePathAnchor[][][]? _moveSaved;
    private Matrix3x2[]? _moveWorldToLocal;
    private Matrix3x2[]? _movePathToDoc;
    private Matrix3x2[]? _moveDocToPath;
    private Vector2[]? _moveSavedBoundsCenter;
    private Vector2[]? _moveSavedTranslation;
    private HashSet<SpritePath>? _moveExcludePaths;
    private Vector2 _dragStartWorld;
    private SnapType _snapType;
    private Vector2 _snapDocLocal;

    // Drag state for curve adjust
    private SpritePath? _curvePath;
    private int _curveContourIndex;
    private int _curveSegmentIndex;
    private SpritePathAnchor[]? _curveSaved;
    private Matrix3x2 _curveInvTransform;
    private Vector2 _curveOffset;
    private Vector2 _curveSavedBoundsCenter;
    private Vector2 _curveSavedTranslation;
    private bool _isCurveDrag;

    protected override bool OnAnchorDragStart(SpritePath path, int contourIndex, int anchorIndex)
    {
        // Collect all paths with selected anchors and snapshot them
        var paths = new List<SpritePath>();
        Editor.Document.Root.CollectPathsWithSelection(paths);
        if (paths.Count == 0) return false;

        Undo.Record(Editor.Document);
        _dragStartWorld = Workspace.DragWorldPosition;
        _movePaths = paths.ToArray();
        _moveSaved = new SpritePathAnchor[paths.Count][][];
        _moveWorldToLocal = new Matrix3x2[paths.Count];
        _movePathToDoc = new Matrix3x2[paths.Count];
        _moveDocToPath = new Matrix3x2[paths.Count];
        _moveSavedBoundsCenter = new Vector2[paths.Count];
        _moveSavedTranslation = new Vector2[paths.Count];
        _moveExcludePaths = new HashSet<SpritePath>(paths);
        _snapType = SnapType.None;

        for (var i = 0; i < paths.Count; i++)
        {
            var p = paths[i];
            _moveSaved[i] = p.SnapshotAllAnchors();

            _movePathToDoc[i] = p.HasTransform ? p.PathTransform : Matrix3x2.Identity;
            Matrix3x2.Invert(_movePathToDoc[i], out _moveDocToPath[i]);

            var pathToWorld = p.HasTransform ? p.PathTransform * Editor.Document.Transform : Editor.Document.Transform;
            Matrix3x2.Invert(pathToWorld, out _moveWorldToLocal[i]);
            _moveSavedBoundsCenter[i] = p.LocalBounds.Center;
            _moveSavedTranslation[i] = p.PathTranslation;
        }

        _isCurveDrag = false;
        return true;
    }

    protected override bool OnSegmentDragStart(SpritePath path, int contourIndex, int segmentIndex, Vector2 position)
    {
        Undo.Record(Editor.Document);

        _curvePath = path;
        _curveContourIndex = contourIndex;
        _curveSegmentIndex = segmentIndex;
        _curveSaved = path.SnapshotAnchors(contourIndex);
        _curveSavedBoundsCenter = path.LocalBounds.Center;
        _curveSavedTranslation = path.PathTranslation;

        var transform = path.HasTransform ? path.PathTransform * Editor.Document.Transform : Editor.Document.Transform;
        Matrix3x2.Invert(transform, out _curveInvTransform);

        // Compute offset from mouse to control point
        var anchors = path.Contours[contourIndex].Anchors;
        var a0 = anchors[segmentIndex];
        var a1 = anchors[(segmentIndex + 1) % anchors.Count];
        var mid = (a0.Position + a1.Position) * 0.5f;
        var dir = a1.Position - a0.Position;
        var len = dir.Length();
        if (len > 0.0001f)
        {
            var perp = new Vector2(-dir.Y, dir.X) / len;
            var controlPoint = mid + perp * a0.Curve;
            var mouseLocal = Vector2.Transform(Workspace.DragWorldPosition, _curveInvTransform);
            _curveOffset = controlPoint - mouseLocal;
        }

        _isCurveDrag = true;
        return true;
    }

    protected override void OnDragUpdate()
    {
        if (_isCurveDrag)
            UpdateCurveDrag();
        else
            UpdateAnchorDrag();
    }

    private void UpdateAnchorDrag()
    {
        if (_movePaths == null) return;

        var mouseWorld = Workspace.MouseWorldPosition;
        _snapType = SnapType.None;
        var snapCorrection = Vector2.Zero; // correction in path-local space (only for ref path)

        // Snap: find where the reference anchor would land, snap it
        if (Input.IsSnapModifierDown(InputScope.All))
        {
            var refSaved = _moveSaved![0];
            var refW2L = _moveWorldToLocal![0];
            var refPathToDoc = _movePathToDoc![0];
            var refDocToPath = _moveDocToPath![0];
            var (refCi, refAi) = FindFirstSelectedIndex(_movePaths[0]);
            if (refAi >= 0)
            {
                var startLocal = Vector2.Transform(_dragStartWorld, refW2L);
                var mouseLocal = Vector2.Transform(mouseWorld, refW2L);
                var candidatePathLocal = refSaved[refCi][refAi].Position + (mouseLocal - startLocal);

                // Convert to doc-local using snapshotted transform
                var candidateDocLocal = Vector2.Transform(candidatePathLocal, refPathToDoc);

                var snappedDocLocal = SnapHelper.Snap(
                    candidateDocLocal, Editor.Document.Root, _moveExcludePaths, out _snapType);

                if (_snapType != SnapType.None)
                {
                    _snapDocLocal = snappedDocLocal;

                    // Convert back to path-local using snapshotted inverse
                    var snappedPathLocal = Vector2.Transform(snappedDocLocal, refDocToPath);
                    snapCorrection = snappedPathLocal - candidatePathLocal;

                }
            }
        }

        for (var pi = 0; pi < _movePaths.Length; pi++)
        {
            var path = _movePaths[pi];
            var saved = _moveSaved![pi];
            var worldToLocal = _moveWorldToLocal![pi];

            var delta = Vector2.Transform(mouseWorld, worldToLocal) -
                        Vector2.Transform(_dragStartWorld, worldToLocal) +
                        snapCorrection;

            for (var ci = 0; ci < path.Contours.Count; ci++)
            {
                var anchors = path.Contours[ci].Anchors;
                for (var i = 0; i < anchors.Count; i++)
                {
                    if (!anchors[i].IsSelected) continue;
                    var a = anchors[i];
                    a.Position = saved[ci][i].Position + delta;
                    anchors[i] = a;
                }
            }

            path.MarkDirty();
            path.UpdateSamples();
            path.UpdateBounds();
            path.PathTranslation = _moveSavedTranslation![pi];
            path.CompensateTranslation(_moveSavedBoundsCenter![pi]);

            // After compensation, the anchor's doc-local position may have drifted
            // from the snap target. Correct PathTranslation to close the gap.
            if (_snapType != SnapType.None && pi == 0)
            {
                var (sCi, sAi) = FindFirstSelectedIndex(path);
                if (sAi >= 0)
                {
                    var currentDocLocal = path.HasTransform
                        ? Vector2.Transform(path.Contours[sCi].Anchors[sAi].Position, path.PathTransform)
                        : path.Contours[sCi].Anchors[sAi].Position;
                    var drift = _snapDocLocal - currentDocLocal;
                    if (drift.LengthSquared() > 0.000001f)
                        path.PathTranslation += drift;
                }
            }
        }

        Editor.MarkDirty();
    }

    private static (int ContourIndex, int AnchorIndex) FindFirstSelectedIndex(SpritePath path)
    {
        for (var ci = 0; ci < path.Contours.Count; ci++)
        {
            var anchors = path.Contours[ci].Anchors;
            for (var i = 0; i < anchors.Count; i++)
                if (anchors[i].IsSelected) return (ci, i);
        }
        return (-1, -1);
    }

    private void UpdateCurveDrag()
    {
        if (_curvePath == null) return;

        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _curveInvTransform);
        var anchors = _curvePath.Contours[_curveContourIndex].Anchors;
        var a0 = anchors[_curveSegmentIndex];
        var a1 = anchors[(_curveSegmentIndex + 1) % anchors.Count];
        var dir = a1.Position - a0.Position;
        var len = dir.Length();
        if (len < 0.0001f) return;

        var perp = new Vector2(-dir.Y, dir.X) / len;
        var mid = (a0.Position + a1.Position) * 0.5f;
        var desiredControlPoint = mouseLocal + _curveOffset;
        var newCurve = Vector2.Dot(desiredControlPoint - mid, perp);

        if (Input.IsSnapModifierDown(InputScope.All))
        {
            var snapThreshold = len * 0.05f;

            // Snap to 0 (straight line)
            if (MathF.Abs(newCurve) < snapThreshold)
            {
                newCurve = 0f;
            }
            else
            {
                // Snap to natural arc (original curve value)
                var naturalCurve = _curveSaved![_curveSegmentIndex].Curve;
                if (MathF.Abs(newCurve - naturalCurve) < snapThreshold)
                    newCurve = naturalCurve;
            }
        }

        _curvePath.SetAnchorCurve(_curveContourIndex, _curveSegmentIndex, newCurve);
        _curvePath.UpdateSamples();
        _curvePath.UpdateBounds();
        _curvePath.PathTranslation = _curveSavedTranslation;
        _curvePath.CompensateTranslation(_curveSavedBoundsCenter);
        Editor.MarkDirty();
    }

    protected override void OnDragCommit()
    {
        FinishDrag();
    }

    protected override void OnDragCancel()
    {
        if (_isCurveDrag && _curvePath != null && _curveSaved != null)
        {
            // Restore curve
            var contour = _curvePath.Contours[_curveContourIndex];
            contour.Anchors.Clear();
            foreach (var a in _curveSaved)
                contour.Anchors.Add(a);
            _curvePath.UpdateSamples();
            _curvePath.UpdateBounds();
            _curvePath.PathTranslation = _curveSavedTranslation;
            _curvePath.CompensateTranslation(_curveSavedBoundsCenter);
        }
        else if (_movePaths != null && _moveSaved != null)
        {
            // Restore anchors
            for (var pi = 0; pi < _movePaths.Length; pi++)
            {
                var path = _movePaths[pi];
                var saved = _moveSaved[pi];
                for (var ci = 0; ci < path.Contours.Count; ci++)
                {
                    var anchors = path.Contours[ci].Anchors;
                    for (var i = 0; i < anchors.Count; i++)
                        anchors[i] = saved[ci][i];
                }
                path.UpdateSamples();
                path.UpdateBounds();
                path.PathTranslation = _moveSavedTranslation![pi];
                path.CompensateTranslation(_moveSavedBoundsCenter![pi]);
            }
        }

        Undo.Cancel();
        Editor.MarkDirty();
        FinishDrag();
    }

    private void FinishDrag()
    {
        _movePaths = null;
        _moveSaved = null;
        _moveWorldToLocal = null;
        _movePathToDoc = null;
        _moveDocToPath = null;
        _moveSavedBoundsCenter = null;
        _moveSavedTranslation = null;
        _moveExcludePaths = null;
        _curvePath = null;
        _curveSaved = null;
        _isCurveDrag = false;
        _snapType = SnapType.None;
        Editor.MarkDirty();
    }

    public override void Draw()
    {
        base.Draw();
        if (_snapType != SnapType.None)
            SnapHelper.DrawSnapIndicator(_snapType, _snapDocLocal, Editor.Document.Transform);
    }
}
