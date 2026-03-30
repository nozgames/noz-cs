//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static partial class AssetPalette
{
    private const int MaxFilteredItems = 1024;
    private const int GridColumns = 5;
    private const float GridCellSize = 60.0f;
    private const float GridLabelHeight = 14.0f;
    private const float GridCellSpacing = 4.0f;

    private static partial class WidgetIds
    {
        public static partial WidgetId Close { get; }
        public static partial WidgetId Search { get; }
        public static partial WidgetId AssetList { get; }
        public static partial WidgetId Item { get; }
        public static partial WidgetId ScrollBar { get; }
    }

    private static string _text = string.Empty;
    private static string _lastFilterText = string.Empty;
    private static readonly Document?[] _filteredItems = new Document?[MaxFilteredItems];
    private static int _filteredCount;
    private static int _selectedIndex;
    private static AssetType _assetTypeFilter;
    private static bool _useGrid;
    private static Action<Document>? _onPicked;
    private static Func<Document, bool>? _filter;

    public static bool IsOpen { get; private set; }

    public static void Open(AssetType assetTypeFilter = default, bool grid = false,
        Action<Document>? onPicked = null, Func<Document, bool>? filter = null)
    {
        if (IsOpen) return;
        if (CommandPalette.IsOpen) return;

        IsOpen = true;
        _assetTypeFilter = assetTypeFilter;
        _useGrid = grid || assetTypeFilter == AssetType.Sprite;
        _onPicked = onPicked;
        _filter = filter;
        _text = string.Empty;
        _lastFilterText = string.Empty;
        _selectedIndex = 0;
        _filteredCount = 0;

        UpdateFilter();
        UI.SetHot(WidgetIds.Search);
    }

    public static void OpenSprites(Action<Document>? onPicked = null, Func<Document, bool>? filter = null) =>
        Open(AssetType.Sprite, grid: true, onPicked: onPicked, filter: filter);

    public static void Close()
    {
        if (!IsOpen) return;

        UI.ClearHot();

        IsOpen = false;
        _text = string.Empty;
        _lastFilterText = string.Empty;
        _filteredCount = 0;
        _selectedIndex = 0;
        _onPicked = null;
        _filter = null;
    }

    public static void Update()
    {
        if (!IsOpen)
            return;

        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Input.ConsumeButton(InputCode.KeyEscape);
            Close();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter))
        {
            SelectCurrent();
            Close();
            return;
        }

        if (_useGrid)
            UpdateGridNavigation();
        else
            UpdateListNavigation();

        if (_text != _lastFilterText)
        {
            UpdateFilter();
            _lastFilterText = _text;
        }
    }

    private static void UpdateListNavigation()
    {
        if (Input.WasButtonPressed(InputCode.KeyUp, allowRepeat: true))
        {
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
            ScrollToSelection();
        }

        if (Input.WasButtonPressed(InputCode.KeyDown, allowRepeat: true))
        {
            _selectedIndex = Math.Min(_filteredCount - 1, _selectedIndex + 1);
            ScrollToSelection();
        }

        if (Input.WasButtonPressed(InputCode.KeyPageUp, allowRepeat: true))
        {
            _selectedIndex = Math.Max(0, _selectedIndex - 10);
            ScrollToSelection();
        }

        if (Input.WasButtonPressed(InputCode.KeyPageDown, allowRepeat: true))
        {
            _selectedIndex = Math.Min(_filteredCount - 1, _selectedIndex + 10);
            ScrollToSelection();
        }

        if (Input.WasButtonPressed(InputCode.KeyHome))
        {
            _selectedIndex = 0;
            ScrollToSelection();
        }

        if (Input.WasButtonPressed(InputCode.KeyEnd))
        {
            _selectedIndex = Math.Max(0, _filteredCount - 1);
            ScrollToSelection();
        }
    }

    private static void UpdateGridNavigation()
    {
        if (Input.WasButtonPressed(InputCode.KeyUp, allowRepeat: true))
        {
            _selectedIndex = Math.Max(0, _selectedIndex - GridColumns);
            ScrollToSelection();
        }

        if (Input.WasButtonPressed(InputCode.KeyDown, allowRepeat: true))
        {
            _selectedIndex = Math.Min(_filteredCount - 1, _selectedIndex + GridColumns);
            ScrollToSelection();
        }

        if (Input.WasButtonPressed(InputCode.KeyLeft, allowRepeat: true))
        {
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
            ScrollToSelection();
        }

        if (Input.WasButtonPressed(InputCode.KeyRight, allowRepeat: true))
        {
            _selectedIndex = Math.Min(_filteredCount - 1, _selectedIndex + 1);
            ScrollToSelection();
        }

        if (Input.WasButtonPressed(InputCode.KeyHome))
        {
            _selectedIndex = 0;
            ScrollToSelection();
        }

        if (Input.WasButtonPressed(InputCode.KeyEnd))
        {
            _selectedIndex = Math.Max(0, _filteredCount - 1);
            ScrollToSelection();
        }
    }

    public static void UpdateUI()
    {
        if (!IsOpen) return;

        using (UI.BeginContainer(WidgetIds.Close))
            if (UI.WasPressed())
                Close();

        using (UI.BeginColumn(EditorStyle.AssetPalette.Root))
        {
            var placeholder = _assetTypeFilter == AssetType.Sprite
                ? "Search sprites..."
                : "Search assets...";

            _text = UI.TextInput(WidgetIds.Search, _text, EditorStyle.CommandPalette.SearchTextBox, placeholder,
                icon: EditorAssets.Sprites.IconSearch);

            UI.Container(EditorStyle.Popup.Separator);

            if (_useGrid)
                GridUI();
            else
                ListUI();
        }
    }

    private static void ListUI()
    {
        using (UI.BeginRow(EditorStyle.AssetPalette.ListContainer))
        {
            using (UI.BeginFlex())
            using (UI.BeginScrollable(WidgetIds.AssetList))
            {
                var cellHeight = EditorStyle.List.ItemHeight;
                var scrollRect = UI.GetElementRect(WidgetIds.AssetList);
                var (startIndex, endIndex) = UI.GetGridCellRange(
                    WidgetIds.AssetList, 1, cellHeight, 0,
                    scrollRect.Height, _filteredCount);

                using (UI.BeginGrid(new GridStyle
                {
                    Columns = 1,
                    CellWidth = scrollRect.Width,
                    CellHeight = cellHeight,
                    VirtualCount = _filteredCount,
                    StartIndex = startIndex,
                }))
                {
                    for (var i = startIndex; i < endIndex; i++)
                    {
                        var doc = _filteredItems[i];
                        if (doc == null) continue;

                        var isSelected = i == _selectedIndex;

                        using (UI.BeginRow(
                            id: WidgetIds.Item + i,
                            style: isSelected
                                ? EditorStyle.CommandPalette.SelectedItem
                                : EditorStyle.CommandPalette.Item))
                        {
                            if (!doc.DrawThumbnail())
                            {
                                var icon = doc.Def.Icon?.Invoke();
                                if (icon != null)
                                    UI.Image(icon, EditorStyle.Control.IconSecondary);
                                else
                                    UI.Spacer(EditorStyle.Control.IconSize);
                            }

                            UI.Text(doc.Name, EditorStyle.Control.Text);

                            if (UI.WasPressed())
                            {
                                _selectedIndex = i;
                                SelectCurrent();
                                Close();
                            }
                        }
                    }
                }
            }

            UI.ScrollBar(WidgetIds.ScrollBar, WidgetIds.AssetList, EditorStyle.CommandPalette.ScrollBar);
        }
    }

    private static void GridUI()
    {
        using (UI.BeginRow(EditorStyle.AssetPalette.GridContainer))
        {
            using (UI.BeginFlex())
            using (UI.BeginScrollable(WidgetIds.AssetList))
            {
                var cellHeight = GridCellSize + GridLabelHeight;
                var viewportHeight = UI.GetElementRect(WidgetIds.AssetList).Height;
                var (startIndex, endIndex) = UI.GetGridCellRange(
                    WidgetIds.AssetList, GridColumns, cellHeight, GridCellSpacing,
                    viewportHeight, _filteredCount);

                using (UI.BeginGrid(new GridStyle
                {
                    Columns = GridColumns,
                    CellWidth = GridCellSize,
                    CellHeight = cellHeight,
                    Spacing = GridCellSpacing,
                    VirtualCount = _filteredCount,
                    StartIndex = startIndex,
                }))
                {
                    for (var i = startIndex; i < endIndex; i++)
                    {
                        var doc = _filteredItems[i];
                        if (doc == null) continue;

                        var isSelected = i == _selectedIndex;

                        using (UI.BeginColumn(WidgetIds.Item + i, new ContainerStyle
                        {
                            Width = GridCellSize,
                            Height = cellHeight,
                        }))
                        {
                            using (UI.BeginContainer(new ContainerStyle
                            {
                                Width = GridCellSize,
                                Height = GridCellSize,
                                Background = isSelected ? EditorStyle.Palette.Active : EditorStyle.Palette.Control,
                                BorderRadius = EditorStyle.Control.BorderRadius,
                                AlignX = Align.Center,
                                AlignY = Align.Center,
                            }))
                            {
                                if (!doc.DrawThumbnail())
                                {
                                    var icon = doc.Def.Icon?.Invoke();
                                    if (icon != null)
                                        UI.Image(icon, new ImageStyle
                                        {
                                            Size = 24,
                                            Color = isSelected ? EditorStyle.Palette.Content : EditorStyle.Palette.SecondaryText,
                                            Align = Align.Center
                                        });
                                }
                            }

                            UI.Text(doc.Name, new TextStyle
                            {
                                FontSize = 7,
                                Color = EditorStyle.Palette.SecondaryText,
                                AlignX = Align.Center,
                                AlignY = Align.Min,
                            });

                            if (UI.WasPressed())
                            {
                                _selectedIndex = i;
                                SelectCurrent();
                                Close();
                            }
                        }
                    }
                }
            }

            UI.ScrollBar(WidgetIds.ScrollBar, WidgetIds.AssetList, EditorStyle.CommandPalette.ScrollBar);
        }
    }

    private static void SelectCurrent()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _filteredCount)
            return;

        var doc = _filteredItems[_selectedIndex];
        if (doc == null)
            return;

        if (_onPicked != null)
        {
            _onPicked(doc);
            return;
        }

        Workspace.ClearSelection();
        Workspace.SetSelected(doc, true);
        Workspace.FrameSelected();
    }

    private static void UpdateFilter()
    {
        _filteredCount = 0;
        _selectedIndex = 0;

        var filter = _text.Trim();

        foreach (var doc in DocumentManager.Documents)
        {
            if (_filteredCount >= MaxFilteredItems)
                break;

            if (!doc.Loaded || !doc.PostLoaded)
                continue;

            if (_assetTypeFilter != default && doc.Def.Type != _assetTypeFilter)
                continue;

            if (_filter != null && !_filter(doc))
                continue;

            if (!CollectionManager.IsDocumentVisible(doc))
                continue;

            if (!string.IsNullOrEmpty(filter) && !doc.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            _filteredItems[_filteredCount++] = doc;
        }

        // Sort alphabetically
        _filteredItems.AsSpan(0, _filteredCount).Sort((a, b) =>
            StringComparer.OrdinalIgnoreCase.Compare(a!.Name, b!.Name));
    }

    private static void ScrollToSelection()
    {
        var scrollableRect = UI.GetElementRect(WidgetIds.AssetList);
        if (scrollableRect.Height <= 0)
            return;

        float itemTop, itemBottom;

        if (_useGrid)
        {
            var row = _selectedIndex / GridColumns;
            var rowHeight = GridCellSize + GridLabelHeight + GridCellSpacing;
            itemTop = row * rowHeight + GridCellSpacing;
            itemBottom = itemTop + rowHeight;
        }
        else
        {
            itemTop = _selectedIndex * EditorStyle.List.ItemHeight;
            itemBottom = itemTop + EditorStyle.List.ItemHeight;
        }

        var currentScroll = UI.GetScrollOffset(WidgetIds.AssetList);
        var viewTop = currentScroll;
        var viewBottom = currentScroll + scrollableRect.Height;
        var newScroll = currentScroll;

        if (itemTop < viewTop)
            newScroll = itemTop;
        else if (itemBottom > viewBottom)
            newScroll = itemBottom - scrollableRect.Height;

        if (newScroll != currentScroll)
            UI.SetScrollOffset(WidgetIds.AssetList, newScroll);
    }
}
