//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PixelRectSelectMode : EditorMode<PixelEditor>
{
    private Vector2Int? _dragStart;
    private Vector2Int _dragCurrent;

    public override void Update()
    {
        EditorCursor.SetCrosshair();

        var mouseWorld = Workspace.MouseWorldPosition;
        var pixel = Editor.WorldToPixelSnapped(mouseWorld);

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
        {
            Editor.ClearSelection();
            return;
        }

        if (_dragStart == null)
        {
            if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
            {
                _dragStart = pixel;
                _dragCurrent = pixel;
            }
        }
        else
        {
            _dragCurrent = pixel;

            if (!Input.IsButtonDown(InputCode.MouseLeft, InputScope.All))
            {
                CommitSelection();
                _dragStart = null;
            }
        }
    }

    private void CommitSelection()
    {
        if (_dragStart == null) return;

        var start = _dragStart.Value;
        var end = _dragCurrent;

        var x0 = Math.Min(start.X, end.X);
        var y0 = Math.Min(start.Y, end.Y);
        var x1 = Math.Max(start.X, end.X) + 1;
        var y1 = Math.Max(start.Y, end.Y) + 1;

        var rect = new RectInt(x0, y0, x1 - x0, y1 - y0);

        // Empty drag = deselect
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            Editor.ClearSelection();
            return;
        }

        SelectionOp op;
        if (Input.IsShiftDown(InputScope.All))
            op = SelectionOp.Add;
        else if (Input.IsAltDown(InputScope.All))
            op = SelectionOp.Subtract;
        else
            op = SelectionOp.Replace;

        Editor.ApplyRectSelection(rect, op);
        Editor.FitSelectionToContent();
    }

    public override void Draw()
    {
        // Draw drag preview
        if (_dragStart == null) return;

        var start = _dragStart.Value;
        var end = _dragCurrent;

        var x0 = Math.Min(start.X, end.X);
        var y0 = Math.Min(start.Y, end.Y);
        var x1 = Math.Max(start.X, end.X) + 1;
        var y1 = Math.Max(start.Y, end.Y) + 1;

        var bounds = Editor.CanvasRect;
        var epr = Editor.EditablePixelRect;
        var cellW = bounds.Width / epr.Width;
        var cellH = bounds.Height / epr.Height;

        var selRect = new Rect(
            bounds.X + (x0 - epr.X) * cellW,
            bounds.Y + (y0 - epr.Y) * cellH,
            (x1 - x0) * cellW,
            (y1 - y0) * cellH);

        Editor.DrawSelectionRect(selRect);
    }
}
