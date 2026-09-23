//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static partial class AssetBrowser
{
    private const int VisibleRows = 8;

    private static partial class WidgetIds
    {
        public static partial WidgetId Trigger { get; }
        public static partial WidgetId Popup { get; }
        public static partial WidgetId Search { get; }
        public static partial WidgetId List { get; }
        public static partial WidgetId Item { get; }
        public static partial WidgetId ScrollBar { get; }
    }

    private static WidgetId _openId;
    private static string _filterText = "";
    private static string _lastFilterText = "";
    private static readonly List<string> _filtered = [];
    private static int _filteredCount;
    private static string? _selected;
    private static string[]? _items;
    private static Func<string, bool>? _thumbnail;
    private static Func<string, string>? _displayName;

    public static bool IsOpen => _openId != WidgetId.None;

    /// <summary>Choose a project asset of one type, displaying its thumbnail.
    /// An empty label enables clearing an optional reference; null means no selection.</summary>
    public static string? Show(WidgetId id, AssetType type, string current, string? emptyLabel = null, Sprite? triggerIcon = null)
    {
        var names = Project.Documents.Where(d => d.Def.Type == type).Select(d => d.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (emptyLabel != null) names = ["", .. names];
        var missing = current.Length > 0 && Project.Find(type, current) == null;
        var label = current.Length == 0 ? emptyLabel ?? "Choose asset…" : current + (missing ? " (missing)" : "");
        return Show(id, names, label,
            name => Project.Find(type, name)?.DrawThumbnail() == true,
            name => name.Length == 0 ? emptyLabel ?? "None" : name, triggerIcon);
    }

    public static string? Show(WidgetId id, string[] items, string label = "+ Add Reference",
        Func<string, bool>? drawThumbnail = null, Func<string, string>? displayName = null, Sprite? triggerIcon = null)
    {
        _selected = null;
        _items = items;
        _thumbnail = drawThumbnail;
        _displayName = displayName;
        var isOpen = _openId == id;

        if (triggerIcon != null)
        {
            if (UI.Button(id, triggerIcon, EditorStyle.Inspector.SectionButton))
            { if (isOpen) Close(); else Open(id); }
        }
        else TriggerUI(id, label, isOpen);

        if (_openId == id)
        {
            UpdateFilter();
            PopupUI(id);
        }

        return _selected;
    }

    private static void Open(WidgetId id)
    {
        _openId = id;
        _filterText = "";
        _lastFilterText = "";
        _filteredCount = 0;
        UI.SetScrollOffset(WidgetIds.List, 0);
        UpdateFilter();
        UI.SetHot(WidgetIds.Search);
    }

    public static void Close()
    {
        if (!IsOpen) return;
        _openId = WidgetId.None;
        _filterText = "";
        _lastFilterText = "";
        _filteredCount = 0;
        _items = null;
        _thumbnail = null;
        _displayName = null;
        _filtered.Clear();
        UI.ClearHot();
    }

    private static void TriggerUI(WidgetId id, string label, bool isOpen)
    {
        var s = EditorStyle.DropDown;
        var flags = ElementTree.GetPrevWidgetFlags(id);
        if (isOpen) flags |= WidgetFlags.Checked;

        if (s.Resolve != null)
            s = s.Resolve(s, flags);

        using (UI.BeginRow(id, new ContainerStyle
        {
            Width = s.Width,
            Height = s.Height,
            Background = s.Color,
            BorderRadius = s.BorderRadius,
            Padding = s.Padding,
            Spacing = s.Spacing,
        }))
        {
            UI.Text(label, new TextStyle
            {
                FontSize = s.FontSize,
                Color = s.ContentColor,
                AlignY = Align.Center,
            });
            UI.Flex();
            if (s.ArrowIcon != null)
                UI.Image(s.ArrowIcon, new ImageStyle
                {
                    Size = s.ArrowSize,
                    Color = s.IconColor.A > 0 ? s.IconColor : s.ContentColor,
                    Align = Align.Center,
                });

            if (UI.WasPressed())
            {
                if (isOpen)
                    Close();
                else
                    Open(id);
            }
        }
    }

    private static void PopupUI(WidgetId id)
    {
        var anchorRect = UI.GetElementWorldRect(id);
        var popupStyle = new PopupStyle
        {
            AnchorX = Align.Min,
            AnchorY = Align.Max,
            PopupAlignX = Align.Min,
            PopupAlignY = Align.Min,
            Spacing = 2.0f,
            ClampToScreen = true,
            AnchorRect = anchorRect,
            MinWidth = MathF.Max(anchorRect.Width, 260),
        };

        using (UI.BeginPopup(WidgetIds.Popup, popupStyle))
        {
            if (UI.IsClosed())
            {
                Close();
                return;
            }

            using (UI.BeginColumn(EditorStyle.Popup.Root with
            {
                Width = Size.Fit,
                Height = Size.Fit,
                MinWidth = MathF.Max(anchorRect.Width, 260),
                Clip = true,
            }))
            {
                SearchUI();
                UI.Container(EditorStyle.Popup.Separator);
                ListUI();
            }
        }
    }

    private static void SearchUI()
    {
        using (UI.BeginRow(EditorStyle.Popup.Item with { Spacing = 4.0f }))
        {
            using (UI.BeginContainer(new ContainerStyle { Width = EditorStyle.Control.Height, Height = EditorStyle.Control.Height }))
                UI.Image(EditorAssets.Sprites.IconSearch, EditorStyle.Control.Icon);

            using (UI.BeginFlex())
                _filterText = UI.TextInput(WidgetIds.Search, _filterText, EditorStyle.CommandPalette.SearchTextBox, "Search...");
        }

        if (_filterText != _lastFilterText)
        {
            UI.SetScrollOffset(WidgetIds.List, 0);
            UpdateFilter();
            _lastFilterText = _filterText;
        }
    }

    private static void ListUI()
    {
        var itemHeight = _thumbnail != null ? 42 : EditorStyle.Control.Height;
        var listHeight = Math.Min(VisibleRows, _filteredCount) * itemHeight;
        if (_filteredCount == 0)
            listHeight = EditorStyle.Control.Height;

        using var row = UI.BeginRow(new ContainerStyle { Height = listHeight });
        using (UI.BeginFlex())
        using (UI.BeginScrollable(WidgetIds.List))
        {
            if (_filteredCount == 0)
            {
                using (UI.BeginContainer(new ContainerStyle
                {
                    Height = EditorStyle.Control.Height,
                    AlignX = Align.Center,
                    AlignY = Align.Center,
                }))
                {
                    UI.Text("No matches", new TextStyle
                    {
                        FontSize = EditorStyle.Control.TextSize,
                        Color = EditorStyle.Palette.SecondaryText,
                        AlignY = Align.Center,
                    });
                }
                return;
            }

            var layout = new CollectionLayout { Columns = 1, ItemHeight = itemHeight };
            using var collection = UI.BeginCollection(WidgetIds.List, layout, _filteredCount, out var start, out var end);
            for (var i = start; i < end; i++)
            {
                var name = _filtered[i];
                var itemId = WidgetIds.Item + i;
                var hovered = UI.IsHovered(itemId);

                using (UI.BeginRow(itemId, EditorStyle.Popup.Item with
                {
                    Spacing = 8,
                    Height = itemHeight,
                    Background = hovered ? EditorStyle.Palette.Active : Color.Transparent,
                    BorderRadius = hovered ? 2 : 0,
                }))
                {
                    using (UI.BeginContainer(new ContainerStyle { Width = itemHeight - 4, Height = itemHeight - 4 }))
                        if (_thumbnail?.Invoke(name) != true) UI.Image(EditorAssets.Sprites.AssetIconSprite, new ImageStyle
                        {
                            Size = EditorStyle.Control.IconSize,
                            Color = hovered ? EditorStyle.Palette.Content : EditorStyle.Palette.SecondaryText,
                            Align = Align.Center,
                        });

                    UI.Text(_displayName?.Invoke(name) ?? name, new TextStyle
                    {
                        FontSize = EditorStyle.Control.TextSize,
                        Color = EditorStyle.Palette.Content,
                        AlignY = Align.Center,
                    });

                    if (UI.WasPressed(itemId))
                    {
                        _selected = name;
                        Close();
                        return;
                    }
                }
            }
        }
        UI.ScrollBar(WidgetIds.ScrollBar, WidgetIds.List, EditorStyle.CommandPalette.ScrollBar);
    }

    private static void UpdateFilter()
    {
        _filteredCount = 0;
        _filtered.Clear();
        if (_items == null) return;

        var filter = _filterText.Trim();
        foreach (var item in _items)
        {
            if (string.IsNullOrEmpty(filter) || (_displayName?.Invoke(item) ?? item).Contains(filter, StringComparison.OrdinalIgnoreCase))
                _filtered.Add(item);
        }
        _filteredCount = _filtered.Count;
    }
}
