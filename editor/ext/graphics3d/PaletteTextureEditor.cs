//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using NoZ;
using NoZ.Editor;

namespace NoZ.Editor.Graphics3D;

internal partial class PaletteTextureEditor : DocumentEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId Size { get; }
        public static partial WidgetId Filter { get; }
        public static partial WidgetId Color { get; }
        public static partial WidgetId FillGradient { get; }
        public static partial WidgetId Clear { get; }
    }

    private readonly List<int> _selected = [];
    private int _hovered = -1;
    private int _selectionGridSize;

    public new PaletteTextureDocument Document => (PaletteTextureDocument)base.Document;
    public override bool ShowInspector => true;
    public override bool ShowWorkspaceGrid => false;

    public PaletteTextureEditor(PaletteTextureDocument document) : base(document)
    {
        _selectionGridSize = document.GridSize;
        Commands =
        [
            new Command("Exit Edit Mode", Workspace.EndEdit, [InputCode.KeyTab]),
            new Command("Fill Gradient", FillGradient, [InputCode.KeyG]),
            new Command("Clear Selected", ClearSelected, [InputCode.KeyDelete])
        ];
    }

    public override void Update()
    {
        Graphics.SetTransform(Document.Transform);
        Document.Draw();

        UpdateHover();
        DrawGrid();
        DrawSelection();
        HandleClick();
    }

    public override void OnUndoRedo()
    {
        base.OnUndoRedo();
        RemapSelection(Document.GridSize);
    }

    public override void InspectorUI()
    {
        using (EditorInspector.BeginSection("PALETTE TEXTURE"))
        {
            if (!EditorInspector.IsSectionCollapsed)
            {
                using (EditorInspector.BeginProperty("Size"))
                    Document.DrawSizeDropDown(WidgetIds.Size, Resize);

                using (EditorInspector.BeginProperty("Color Grid"))
                    UI.Text($"{Document.GridSize} × {Document.GridSize}");

                using (EditorInspector.BeginProperty("Cell Pixels"))
                    UI.Text($"{PaletteTextureDocument.CellPixelSize} × {PaletteTextureDocument.CellPixelSize}");

                using (EditorInspector.BeginProperty("Filter"))
                    Document.DrawFilterDropDown(WidgetIds.Filter, SetFilter);
            }
        }

        using (EditorInspector.BeginSection("SELECTION"))
        {
            if (EditorInspector.IsSectionCollapsed)
                return;

            if (_selected.Count == 0)
            {
                UI.Text("Click a cell to select it. Shift-click cells to build an ordered gradient path.", EditorStyle.Text.Secondary);
                return;
            }

            var active = _selected[^1];
            var x = active % Document.GridSize;
            var y = active / Document.GridSize;

            using (EditorInspector.BeginProperty(_selected.Count == 1 ? "Cell" : "Cells"))
                UI.Text(_selected.Count == 1 ? $"{x}, {y}" : _selected.Count.ToString());

            using (EditorInspector.BeginProperty("Color"))
            {
                var color = EditorInspector.ColorField(WidgetIds.Color, Document.GetPixel(active).ToColor(), Document);
                if (UI.WasChanged())
                    Document.SetPixels(_selected, color);
                if (UI.WasChangeCancelled())
                    Undo.Cancel();
            }

            using (UI.BeginRow(EditorStyle.Control.Spacing))
            {
                using (UI.BeginFlex())
                {
                    if (UI.Button(WidgetIds.Clear, "Clear", EditorStyle.Button.Secondary with { Width = Size.Percent(1) }))
                        ClearSelected();
                }

                if (_selected.Count >= 2)
                {
                    using (UI.BeginFlex())
                    {
                        if (UI.Button(WidgetIds.FillGradient, "Fill Gradient", EditorStyle.Button.Primary with { Width = Size.Percent(1) }))
                            FillGradient();
                    }
                }
            }
        }
    }

    private void UpdateHover()
    {
        _hovered = -1;
        if (!UI.IsHovered(Workspace.SceneWidgetId))
            return;

        var mouse = Workspace.MouseWorldPosition - Document.Position;
        var x = (int)MathF.Floor((mouse.X + 0.5f) * Document.GridSize);
        var y = (int)MathF.Floor((mouse.Y + 0.5f) * Document.GridSize);
        if ((uint)x >= (uint)Document.GridSize || (uint)y >= (uint)Document.GridSize)
            return;

        _hovered = y * Document.GridSize + x;
    }

    private void HandleClick()
    {
        if (!Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All) ||
            !UI.IsHovered(Workspace.SceneWidgetId))
            return;

        var additive = Input.IsShiftDown(InputScope.All) || Input.IsCtrlDown(InputScope.All);
        if (_hovered < 0)
        {
            if (!additive)
                _selected.Clear();
            return;
        }

        if (!additive)
        {
            _selected.Clear();
            _selected.Add(_hovered);
            return;
        }

        var existing = _selected.IndexOf(_hovered);
        if (existing >= 0)
            _selected.RemoveAt(existing);
        else
            _selected.Add(_hovered);
    }

    private void DrawGrid()
    {
        var size = Document.GridSize;
        var bounds = Document.Bounds;
        var camera = Workspace.Camera;
        var screenOrigin = camera.ScreenToWorld(Vector2.Zero);
        // Gizmos.DrawLine interprets width as a half-width and scales it with
        // workspace zoom. Use full one-screen-pixel strokes for this grid.
        var lineWidth = MathF.Min(
            Vector2.Distance(screenOrigin, camera.ScreenToWorld(Vector2.UnitX)),
            bounds.Width / size * 0.25f);
        var lineHeight = MathF.Min(
            Vector2.Distance(screenOrigin, camera.ScreenToWorld(Vector2.UnitY)),
            bounds.Height / size * 0.25f);

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetColor(EditorStyle.Workspace.BoundsColor.WithAlpha(size <= 32 ? 0.8f : 0.4f));

            for (var i = 0; i <= size; i++)
            {
                // Inset the perimeter strokes so all four edges remain fully
                // inside the texture bounds. Every horizontal/vertical stroke
                // spans the entire canvas, including the right/bottom edges.
                var x = i == size ? bounds.Right - lineWidth : bounds.Left + bounds.Width * i / size;
                var y = i == size ? bounds.Bottom - lineHeight : bounds.Top + bounds.Height * i / size;
                if (i > 0 && i < size)
                {
                    x -= lineWidth * 0.5f;
                    y -= lineHeight * 0.5f;
                }

                Graphics.Draw(x, bounds.Top, lineWidth, bounds.Height);
                Graphics.Draw(bounds.Left, y, bounds.Width, lineHeight);
            }
        }
    }

    private void DrawSelection()
    {
        var cellSize = 1f / Document.GridSize;

        using (Gizmos.PushState(EditorLayer.Selection))
        {
            Graphics.SetTransform(Document.Transform);

            if (_selected.Count >= 2)
            {
                Graphics.SetColor(EditorStyle.Palette.Primary.WithAlpha(0.7f));
                for (var i = 0; i < _selected.Count - 1; i++)
                    Gizmos.DrawLine(GetCellCenter(_selected[i]), GetCellCenter(_selected[i + 1]), EditorStyle.BoxSelect.LineWidth);
            }

            for (var i = 0; i < _selected.Count; i++)
            {
                Graphics.SetColor(i == _selected.Count - 1
                    ? EditorStyle.Palette.Primary
                    : EditorStyle.Palette.Primary.WithAlpha(0.75f));
                Gizmos.DrawRect(GetCellRect(_selected[i]), EditorStyle.BoxSelect.LineWidth);
            }

            if (_hovered >= 0 && !_selected.Contains(_hovered))
            {
                Graphics.SetColor(EditorStyle.Palette.Primary.WithAlpha(0.45f));
                Gizmos.DrawRect(GetCellRect(_hovered), Math.Min(EditorStyle.Workspace.DocumentBoundsLineWidth, cellSize * 0.1f));
            }
        }
    }

    private Rect GetCellRect(int index)
    {
        var cellSize = 1f / Document.GridSize;
        var x = index % Document.GridSize;
        var y = index / Document.GridSize;
        return new Rect(-0.5f + x * cellSize, -0.5f + y * cellSize, cellSize, cellSize);
    }

    private Vector2 GetCellCenter(int index)
    {
        var rect = GetCellRect(index);
        return rect.Center;
    }

    private void Resize(int size)
    {
        if (size == Document.Size || !Document.CanResize(size))
            return;

        Undo.Record(Document);
        Document.Resize(size);
        RemapSelection(Document.GridSize);
    }

    private void SetFilter(TextureFilter filter)
    {
        if (filter == Document.Filter)
            return;
        Undo.Record(Document);
        Document.SetFilter(filter);
    }

    private void FillGradient()
    {
        if (_selected.Count < 2)
            return;
        Undo.Record(Document);
        Document.FillGradient(_selected);
    }

    private void ClearSelected()
    {
        if (_selected.Count == 0)
            return;
        Undo.Record(Document);
        Document.SetPixels(_selected, Color32.Transparent);
    }

    private void RemapSelection(int newGridSize)
    {
        var selectedCoordinates = _selected
            .Select(index => new Vector2Int(index % _selectionGridSize, index / _selectionGridSize))
            .ToArray();

        _selected.Clear();
        foreach (var coordinate in selectedCoordinates)
        {
            if (coordinate.X < newGridSize && coordinate.Y < newGridSize)
                _selected.Add(coordinate.Y * newGridSize + coordinate.X);
        }

        _selectionGridSize = newGridSize;
    }
}
