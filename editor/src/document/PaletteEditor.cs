//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal partial class PaletteEditor : DocumentEditor
{
    private const int Columns = 8;
    private const float CellSize = 1f / Columns;
    private const float DragThreshold = 4f;
    private const float DropLineHeight = 2;

    private enum DropZone { Before, After }

    private static partial class WidgetIds
    {
        public static partial WidgetId ColorButton { get; }
        public static partial WidgetId ColorNameInput { get; }
        public static partial WidgetId OutlinerPanel { get; }
        public static partial WidgetId OutlinerRow { get; }
        public static partial WidgetId AddColorButton { get; }
        public static partial WidgetId DeleteColorButton { get; }
        public static partial WidgetId RenameInput { get; }
    }

    public new PaletteDocument Document => (PaletteDocument)base.Document;

    public override bool ShowInspector => _selectedColorIndex >= 0;

    private int _selectedColorIndex = -1;
    private int _hoveredColorIndex = -1;
    private int _lastVersion;

    // Outliner rename state
    private int _renameIndex = -1;
    private string? _renameText;

    // Outliner drag state
    private bool _outlinerDragging;
    private int _dragSourceIndex = -1;
    private int _dropTargetIndex = -1;
    private DropZone _dropZone;
    private Vector2 _dragStartPos;

    private struct OutlinerRowInfo
    {
        public int ColorIndex;
        public int WidgetIndex;
    }

    private readonly List<OutlinerRowInfo> _outlinerRows = [];

    public PaletteEditor(PaletteDocument doc) : base(doc)
    {
        Commands =
        [
            new Command("Exit Edit Mode", Workspace.EndEdit, [InputCode.KeyTab]),
            new Command("Delete Color", DeleteSelectedColor, [InputCode.KeyDelete]),
        ];
    }

    public override void OnUndoRedo()
    {
        if (_selectedColorIndex >= Document.ColorCount)
            _selectedColorIndex = Document.ColorCount > 0 ? Document.ColorCount - 1 : -1;
    }

    public override void Update()
    {
        if (Document.Version != _lastVersion)
        {
            _lastVersion = Document.Version;
            PaletteManager.ReloadPaletteColors();
        }

        Graphics.SetTransform(Document.Transform);
        Document.DrawBounds(selected: false);
        Document.Draw();

        UpdateHover();
        DrawHighlights();
        HandleClick();
    }

    private void UpdateHover()
    {
        _hoveredColorIndex = -1;
        if (!UI.IsHovered(Workspace.SceneWidgetId)) return;

        var mouseWorld = Workspace.MouseWorldPosition;
        var localX = mouseWorld.X - Document.Position.X;
        var localY = mouseWorld.Y - Document.Position.Y;

        var col = (int)((localX + 0.5f) / CellSize);
        var row = (int)((localY + 0.5f) / CellSize);

        if (col < 0 || col >= Columns || row < 0) return;

        var index = row * Columns + col;
        if (index >= 0 && index < Document.ColorCount)
            _hoveredColorIndex = index;
    }

    private void DrawHighlights()
    {
        var rows = Columns;

        // Grid lines
        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetColor(EditorStyle.Workspace.BoundsColor);

            for (var c = 0; c <= Columns; c++)
            {
                var x = -0.5f + c * CellSize;
                Gizmos.DrawLine(new(x, -0.5f), new(x, -0.5f + rows * CellSize), EditorStyle.Workspace.DocumentBoundsLineWidth);
            }

            for (var r = 0; r <= rows; r++)
            {
                var y = -0.5f + r * CellSize;
                Gizmos.DrawLine(new(-0.5f, y), new(-0.5f + Columns * CellSize, y), EditorStyle.Workspace.DocumentBoundsLineWidth);
            }
        }

        // Selection and hover
        using (Gizmos.PushState(EditorLayer.Selection))
        {
            Graphics.SetTransform(Document.Transform);

            if (_selectedColorIndex >= 0 && _selectedColorIndex < Document.ColorCount)
            {
                var rect = GetCellRect(_selectedColorIndex);
                Graphics.SetColor(EditorStyle.Palette.Primary);
                Gizmos.DrawRect(rect, EditorStyle.BoxSelect.LineWidth);
            }

            if (_hoveredColorIndex >= 0 && _hoveredColorIndex != _selectedColorIndex)
            {
                var rect = GetCellRect(_hoveredColorIndex);
                Graphics.SetColor(EditorStyle.Palette.Primary.WithAlpha(0.5f));
                Gizmos.DrawRect(rect, EditorStyle.Workspace.DocumentBoundsLineWidth);
            }
        }
    }

    private void HandleClick()
    {
        if (!Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All)) return;
        if (!UI.IsHovered(Workspace.SceneWidgetId)) return;

        if (_hoveredColorIndex >= 0)
            _selectedColorIndex = _hoveredColorIndex;
    }

    private static Rect GetCellRect(int index)
    {
        var col = index % Columns;
        var row = index / Columns;
        return new Rect(-0.5f + col * CellSize, -0.5f + row * CellSize, CellSize, CellSize);
    }

    #region Inspector

    public override void InspectorUI()
    {
        if (_selectedColorIndex < 0 || _selectedColorIndex >= Document.ColorCount) return;

        using (Inspector.BeginSection("COLOR"))
        {
            if (Inspector.IsSectionCollapsed) return;

            using (Inspector.BeginProperty("Name"))
            {
                var currentName = Document.ColorNames[_selectedColorIndex] ?? "";
                var name = UI.TextInput(WidgetIds.ColorNameInput, currentName, EditorStyle.TextInput);
                if (name != currentName)
                {
                    Undo.Record(Document);
                    Document.ColorNames[_selectedColorIndex] = string.IsNullOrWhiteSpace(name) ? null : name;
                }
            }

            using (Inspector.BeginProperty("Color"))
            {
                var color = EditorUI.ColorButton(WidgetIds.ColorButton, Document.Colors[_selectedColorIndex]);
                if (UI.WasChangeStarted()) Undo.Record(Document);
                if (UI.WasChanged()) Document.Colors[_selectedColorIndex] = color;
                if (UI.WasChangeCancelled()) Undo.Cancel();
            }
        }
    }

    #endregion

    #region Outliner

    public override void OutlinerUI()
    {
        HandleRenameInput();
        UpdateOutlinerDrag();
        _outlinerRows.Clear();

        // F2 to begin rename on selected item
        if (_renameIndex < 0 && _selectedColorIndex >= 0 &&
            Input.WasButtonPressed(InputCode.KeyF2, InputScope.All))
        {
            BeginRename(_selectedColorIndex);
        }

        void AddButton()
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(WidgetIds.AddColorButton, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
                AddColor();
            ElementTree.EndAlign();
        }

        using (Outliner.BeginSection("COLORS", content: AddButton, collapsible: false))
        {
            ElementTree.BeginColumn(0);
            for (var i = 0; i < Document.ColorCount; i++)
            {
                var widgetIndex = i;
                var rowId = WidgetIds.OutlinerRow + widgetIndex;
                _outlinerRows.Add(new OutlinerRowInfo { ColorIndex = i, WidgetIndex = widgetIndex });

                var isSelected = i == _selectedColorIndex;
                var isRenaming = _renameIndex == i;
                var isDragSource = _outlinerDragging && _dragSourceIndex == i;
                var dropBefore = _outlinerDragging && _dropTargetIndex == i && _dropZone == DropZone.Before;
                var dropAfter = _outlinerDragging && _dropTargetIndex == i && _dropZone == DropZone.After;

                if (isDragSource)
                    UI.BeginOpacity(0.35f);

                var bg = isSelected ? EditorStyle.Palette.Active : Color.Transparent;

                ElementTree.BeginTree();
                ElementTree.BeginWidget(rowId);
                ElementTree.BeginSize(Size.Default, EditorStyle.Control.Height);
                ElementTree.BeginFill(bg);

                // Drop indicator (overlaid inside the row)
                if (dropBefore || dropAfter)
                {
                    ElementTree.BeginAlign(Align.Min, dropAfter ? Align.Max : Align.Min);
                    ElementTree.BeginSize(Size.Default, new Size(DropLineHeight));
                    ElementTree.BeginFill(EditorStyle.Palette.Primary);
                    ElementTree.EndFill();
                    ElementTree.EndSize();
                    ElementTree.EndAlign();
                }

                ElementTree.BeginPadding(EditorStyle.Item.Padding);
                ElementTree.BeginRow(EditorStyle.Control.Spacing);

                // Color swatch
                UI.Container(new ContainerStyle
                {
                    Width = EditorStyle.Control.Height - 4,
                    Height = EditorStyle.Control.Height - 4,
                    Background = Document.Colors[i],
                    BorderRadius = 2,
                    Margin = EdgeInsets.TopLeft(2),
                });

                // Name (inline rename or static text)
                ElementTree.BeginFlex();
                if (isRenaming)
                {
                    ElementTree.BeginMargin(EdgeInsets.TopLeft(2, -2));
                    _renameText = UI.TextInput(WidgetIds.RenameInput, _renameText ?? "", EditorStyle.SpriteEditor.OutlinerRename);
                    ElementTree.EndMargin();

                    if (UI.HotExit())
                        CommitRename();
                }
                else
                {
                    var displayName = Document.ColorNames[i] ?? $"Color {i}";
                    UI.Text(displayName, EditorStyle.Text.Primary);
                }
                ElementTree.EndFlex();

                ElementTree.EndRow();
                ElementTree.EndPadding();
                ElementTree.EndFill();
                ElementTree.EndTree();

                // Click handling
                if (UI.WasPressed(rowId) && !isRenaming)
                {
                    if (_renameIndex >= 0) CommitRename();
                    _selectedColorIndex = i;
                    _dragSourceIndex = i;
                    _dragStartPos = Input.MousePosition;
                }

                if (isDragSource)
                    UI.EndOpacity();
            }
            ElementTree.EndColumn();
        }
    }

    private void AddColor()
    {
        if (Document.ColorCount >= PaletteDocument.MaxColors) return;

        Undo.Record(Document);
        var newColor = _selectedColorIndex >= 0 && _selectedColorIndex < Document.ColorCount
            ? Document.Colors[_selectedColorIndex]
            : Color.White;
        Document.Colors[Document.ColorCount] = newColor;
        Document.ColorNames[Document.ColorCount] = null;
        Document.ColorCount++;
        _selectedColorIndex = Document.ColorCount - 1;
    }

    private void DeleteSelectedColor()
    {
        if (_selectedColorIndex < 0 || _selectedColorIndex >= Document.ColorCount) return;
        if (Document.ColorCount <= 1) return;

        Undo.Record(Document);

        // Shift colors down
        for (var i = _selectedColorIndex; i < Document.ColorCount - 1; i++)
        {
            Document.Colors[i] = Document.Colors[i + 1];
            Document.ColorNames[i] = Document.ColorNames[i + 1];
        }
        Document.ColorCount--;

        if (_selectedColorIndex >= Document.ColorCount)
            _selectedColorIndex = Document.ColorCount - 1;
    }

    #endregion

    #region Outliner Rename

    private void HandleRenameInput()
    {
        if (_renameIndex < 0) return;

        if (Input.WasButtonPressed(InputCode.KeyEnter, InputScope.All))
        {
            Input.ConsumeButton(InputCode.KeyEnter);
            CommitRename();
        }
        else if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
        {
            Input.ConsumeButton(InputCode.KeyEscape);
            CancelRename();
        }
    }

    private void BeginRename(int index)
    {
        _renameIndex = index;
        _renameText = Document.ColorNames[index] ?? "";
        UI.SetHot(WidgetIds.RenameInput);
    }

    private void CommitRename()
    {
        if (_renameIndex >= 0 && _renameIndex < Document.ColorCount && _renameText != null)
        {
            var currentName = Document.ColorNames[_renameIndex] ?? "";
            if (_renameText != currentName)
            {
                Undo.Record(Document);
                Document.ColorNames[_renameIndex] = string.IsNullOrWhiteSpace(_renameText) ? null : _renameText;
            }
        }

        _renameIndex = -1;
        _renameText = null;
    }

    private void CancelRename()
    {
        _renameIndex = -1;
        _renameText = null;
    }

    #endregion

    #region Outliner Drag-Drop

    private void UpdateOutlinerDrag()
    {
        if (_dragSourceIndex < 0)
        {
            _outlinerDragging = false;
            _dropTargetIndex = -1;
            return;
        }

        if (!Input.IsButtonDownRaw(InputCode.MouseLeft))
        {
            if (_outlinerDragging && _dropTargetIndex >= 0 && _dropTargetIndex != _dragSourceIndex)
                CommitDrop();

            CancelDrag();
            return;
        }

        if (!_outlinerDragging)
        {
            if (Vector2.Distance(Input.MousePosition, _dragStartPos) < DragThreshold)
                return;
            _outlinerDragging = true;
        }

        FindDropTarget();
    }

    private void FindDropTarget()
    {
        _dropTargetIndex = -1;
        var mouseWorld = UI.MouseWorldPosition;

        for (var i = 0; i < _outlinerRows.Count; i++)
        {
            var row = _outlinerRows[i];
            var rect = UI.GetElementWorldRect(WidgetIds.OutlinerRow + row.WidgetIndex);
            if (rect.Width <= 0) continue;

            if (mouseWorld.Y >= rect.Y && mouseWorld.Y <= rect.Bottom)
            {
                var relY = (mouseWorld.Y - rect.Y) / rect.Height;

                // Normalize "after N" to "before N+1" so the line is always in a fixed spot
                if (relY >= 0.5f && row.ColorIndex + 1 < Document.ColorCount)
                {
                    _dropTargetIndex = row.ColorIndex + 1;
                    _dropZone = DropZone.Before;
                }
                else if (relY < 0.5f)
                {
                    _dropTargetIndex = row.ColorIndex;
                    _dropZone = DropZone.Before;
                }
                else
                {
                    // Last item, after
                    _dropTargetIndex = row.ColorIndex;
                    _dropZone = DropZone.After;
                }

                // No-op: dropping adjacent to source where nothing would move
                if (_dropTargetIndex == _dragSourceIndex ||
                    (_dropZone == DropZone.Before && _dropTargetIndex == _dragSourceIndex + 1))
                {
                    _dropTargetIndex = -1;
                }
                break;
            }
        }
    }

    private void CommitDrop()
    {
        if (_dragSourceIndex < 0 || _dropTargetIndex < 0) return;

        // Compute the destination index
        var destIndex = _dropTargetIndex;
        if (_dropZone == DropZone.After) destIndex++;
        if (destIndex > _dragSourceIndex) destIndex--;

        if (destIndex == _dragSourceIndex) return;

        Undo.Record(Document);

        var color = Document.Colors[_dragSourceIndex];
        var name = Document.ColorNames[_dragSourceIndex];

        if (_dragSourceIndex < destIndex)
        {
            for (var i = _dragSourceIndex; i < destIndex; i++)
            {
                Document.Colors[i] = Document.Colors[i + 1];
                Document.ColorNames[i] = Document.ColorNames[i + 1];
            }
        }
        else
        {
            for (var i = _dragSourceIndex; i > destIndex; i--)
            {
                Document.Colors[i] = Document.Colors[i - 1];
                Document.ColorNames[i] = Document.ColorNames[i - 1];
            }
        }

        Document.Colors[destIndex] = color;
        Document.ColorNames[destIndex] = name;
        _selectedColorIndex = destIndex;
    }

    private void CancelDrag()
    {
        _outlinerDragging = false;
        _dragSourceIndex = -1;
        _dropTargetIndex = -1;
    }

    #endregion
}
