//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public struct PopupMenuItem
{
    public string? Label;
    public Action? Handler;
    public int Level;
    public KeyBinding? Shortcut;
    public Sprite? Icon;
    public Func<bool>? GetEnabled;
    public Func<bool>? GetChecked;
    public bool ShowChecked;
    public bool ShowIcons;

    public readonly bool IsEnabled => GetEnabled?.Invoke() ?? true;
    public readonly bool IsChecked => GetChecked?.Invoke() ?? false;

    public static PopupMenuItem Item(
        string label,
        Action handler,
        KeyBinding? shortcut = null,
        int level = 0,
        Func<bool>? enabled = null, Func<bool>? isChecked = null,
        Sprite? icon = null) =>
        new() { Label = label, Handler = handler, Level = level, Shortcut = shortcut, GetEnabled = enabled, GetChecked = isChecked, Icon = icon, ShowChecked = true };

    public static PopupMenuItem Submenu(string label, int level = 0, bool showChecked = false, bool showIcons = true, Func<bool>? isChecked = null) =>
        new() { Label = label, Handler = null, Level = level, ShowChecked = showChecked, ShowIcons = showIcons, GetChecked = isChecked };

    public static PopupMenuItem Separator(int level = 0) =>
        new() { Label = null, Handler = null, Level = level };
}

public struct PopupMenuStyle()
{
    // Menu container
    public Color BackgroundColor = Color.FromRgb(0x2D2D2D);
    public BorderRadius BorderRadius = 6f;
    public float BorderWidth = 1;
    public Color BorderColor = Color.FromRgb(0x3D3D3D);
    public EdgeInsets Padding = EdgeInsets.Symmetric(4, 0);
    public float MinWidth = 140;

    // Items
    public float ItemHeight = Style.Widget.Height;
    public EdgeInsets ItemPadding = new(0, 4, 0, 4);
    public float ItemContentPadding = 4;
    public float ItemContentSpacing = Style.Widget.Spacing;
    public Color ItemHoverColor = Color.FromRgb(0x3D3D3D);

    // Text
    public float FontSize = Style.Widget.FontSize;
    public Color TextColor = Style.Palette.Content;
    public Color DisabledTextColor = Color.FromRgb(0x666666);
    public Font? Font = null;

    // Icons
    public float IconSize = Style.Widget.IconSize;
    public float CheckWidth = 20;

    // Separator
    public float SeparatorHeight = 1;
    public EdgeInsets SeparatorMargin = new(2, 4, 2, 4);
    public Color SeparatorColor = Color.FromRgb(0x3D3D3D);

    // Title
    public float TitleFontSize = 16;
    public Color TitleColor = Color.FromRgb(0x999999);
    public EdgeInsets TitlePadding = EdgeInsets.LeftRight(4);

    // Shortcut text
    public Color ShortcutColor = Color.FromRgb(0x999999);

    // Submenu
    public float SubmenuSpacing = 6;
    public Sprite? SubmenuIcon = null;
    public Sprite? CheckIcon = null;
}

public static partial class UI
{
    internal static partial class PopupHelper
    {
        private const int MaxItems = 64;
        private const int MaxSubmenuDepth = 8;
        private const float SubmenuDelay = 0.3f;

        private struct LevelState
        {
            public int OpenSubmenu;
            public bool ShowChecked;
            public bool ShowIcons;
        }

        private static partial class WidgetIds
        {
            public static partial WidgetId Menu { get; }
            public static partial WidgetId Item { get; }
        }

        private static WidgetId _id;
        private static bool _visible;
        private static PopupMenuItem[] _items = new PopupMenuItem[MaxItems];
        private static int _itemCount;
        private static string? _title;
        private static InputScope _scope;
        private static PopupStyle _popupStyle;
        private static PopupMenuStyle _menuStyle;
        private static readonly LevelState[] _levels = new LevelState[MaxSubmenuDepth];
        private static int _pendingSubmenuIndex = -1;
        private static int _pendingSubmenuLevel = -1;
        private static float _submenuTimer;

        public static bool IsVisible => _visible;

        public static void Init()
        {
        }

        public static void Shutdown()
        {
            _visible = false;
            _itemCount = 0;
            _title = null;
        }

        public static void Open(
            WidgetId id,
            ReadOnlySpan<PopupMenuItem> items,
            in PopupMenuStyle menuStyle,
            string? title = null) => Open(
                id,
                items,
                menuStyle,
                new PopupStyle()
                {
                    AnchorRect = new Rect(UI.ScreenToUI(Input.MousePosition), Vector2.Zero)
                },
                title);

        public static void Open(
            WidgetId id,
            ReadOnlySpan<PopupMenuItem> items,
            in PopupMenuStyle menuStyle,
            PopupStyle popupStyle,
            string? title = null)
        {
            _itemCount = Math.Min(items.Length, MaxItems);
            for (var i = 0; i < _itemCount; i++)
                _items[i] = items[i];

            _id = id;
            _title = title;
            _popupStyle = popupStyle;
            _menuStyle = menuStyle;
            _visible = true;
            for (var i = 0; i < MaxSubmenuDepth; i++)
                _levels[i] = new LevelState { OpenSubmenu = -1, ShowChecked = true, ShowIcons = true };
            _levels[0].ShowChecked = popupStyle.ShowChecked;
            _levels[0].ShowIcons = popupStyle.ShowIcons;
            UI.ClearHot();
            ResetSubmenuTimer();

            _scope = Input.PushScope();
            Input.ConsumeButton(InputCode.MouseLeft);
        }

        public static void Close()
        {
            _visible = false;
            _itemCount = 0;
            _title = null;
            _id = WidgetId.None;
            UI.ClearHot();
            ResetSubmenuTimer();
            Input.PopScope(_scope);
        }

        public static bool IsOpen(WidgetId id) => _visible && _id == id;

        private static bool UpdateSubmenuHover(int level, int targetIndex)
        {
            if (_pendingSubmenuLevel == level && _pendingSubmenuIndex == targetIndex)
            {
                _submenuTimer += Time.DeltaTime;
            }
            else
            {
                _pendingSubmenuLevel = level;
                _pendingSubmenuIndex = targetIndex;
                _submenuTimer = 0;
            }

            return _submenuTimer >= SubmenuDelay;
        }

        private static void ResetSubmenuTimer()
        {
            _pendingSubmenuIndex = -1;
            _pendingSubmenuLevel = -1;
            _submenuTimer = 0;
        }

        public static void Update()
        {
            if (!_visible)
                return;

            if (Input.WasButtonPressed(InputCode.KeyEscape, _scope))
            {
                Input.ConsumeButton(InputCode.KeyEscape);

                // Close deepest open submenu first, or close menu if none open
                var closedSubmenu = false;
                for (var level = MaxSubmenuDepth - 1; level >= 0; level--)
                {
                    if (_levels[level].OpenSubmenu >= 0)
                    {
                        _levels[level].OpenSubmenu = -1;
                        closedSubmenu = true;
                        break;
                    }
                }

                if (!closedSubmenu)
                    Close();
            }
        }

        public static void UpdateUI()
        {
            if (!_visible)
                return;

            Action? executed = null;
            var shouldClose = false;

            MenuUI(0, -1, _popupStyle, ref executed, ref shouldClose);

            if (executed != null)
            {
                Close();
                executed();
            }
            else if (shouldClose)
            {
                Input.ConsumeButton(InputCode.MouseLeft);
                Close();
            }
        }

        private static bool HasChildren(int index)
        {
            if (index < 0 || index >= _itemCount - 1) return false;
            return _items![index + 1].Level > _items[index].Level;
        }

        private static void SeparatorUI()
        {
            ElementTree.BeginTree();
            ElementTree.BeginPadding(_menuStyle.SeparatorMargin);
            ElementTree.BeginSize(Size.Default, new Size(_menuStyle.SeparatorHeight));
            ElementTree.BeginFill(_menuStyle.SeparatorColor);
            ElementTree.EndTree();
        }

        private static bool ItemUI(
            WidgetId itemId,
            ref PopupMenuItem item,
            bool enabled,
            bool showChecked,
            bool showIcons)
        {
            var font = _menuStyle.Font ?? _defaultFont!;

            ElementTree.BeginTree();
            ElementTree.BeginWidget(itemId);

            var flags = ElementTree.GetWidgetFlags();
            var hovered = flags.HasFlag(WidgetFlags.Hovered) && enabled;
            var pressed = flags.HasFlag(WidgetFlags.Pressed) && enabled;
            var textColor = enabled ? _menuStyle.TextColor : _menuStyle.DisabledTextColor;

            ElementTree.BeginSize(Size.Default, new Size(_menuStyle.ItemHeight));
            ElementTree.BeginFill(hovered ? _menuStyle.ItemHoverColor : Color.Transparent);
            ElementTree.BeginPadding(_menuStyle.ItemPadding);
            ElementTree.BeginRow(_menuStyle.ItemContentSpacing);

            // Check icon column
            if (showChecked)
            {
                if (item.IsChecked && _menuStyle.CheckIcon != null)
                    ElementTree.Image(_menuStyle.CheckIcon, new Size2(_menuStyle.CheckWidth, Size.Default), ImageStretch.Uniform, textColor, align: new Align2(Align.Center, Align.Center));
                else
                    ElementTree.Spacer(_menuStyle.CheckWidth);
            }

            // Item icon
            if (showIcons)
            {
                if (item.Icon != null)
                    ElementTree.Image(item.Icon, new Size2(_menuStyle.IconSize, _menuStyle.IconSize), ImageStretch.Uniform, textColor, align: new Align2(Align.Center, Align.Center));
                else
                    ElementTree.Spacer(_menuStyle.IconSize);
            }

            // Label
            ElementTree.Text(item.Label!, font, _menuStyle.FontSize, textColor, new Align2(Align.Min, Align.Center));

            // Shortcut
            if (item.Shortcut.HasValue)
            {
                ElementTree.Flex();
                var shortcut = FormatShortcut(item.Shortcut.Value);
                ElementTree.Text(shortcut, font, _menuStyle.FontSize, hovered ? textColor : _menuStyle.ShortcutColor, new Align2(Align.Max, Align.Center));
            }

            ElementTree.EndTree();

            return pressed;
        }

        private static string FormatShortcut(KeyBinding shortcut)
        {
            var parts = new System.Text.StringBuilder();
            if (shortcut.Ctrl) parts.Append("Ctrl+");
            if (shortcut.Alt) parts.Append("Alt+");
            if (shortcut.Shift) parts.Append("Shift+");
            parts.Append(shortcut.Key.ToDisplayString());
            return parts.ToString();
        }

        private static void MenuUI(
            int level,
            int parentIndex,
            PopupStyle style,
            ref Action? executed,
            ref bool shouldClose)
        {
            var startIndex = level == 0 ? 0 : parentIndex + 1;
            var parentLevel = level == 0 ? -1 : _items![parentIndex].Level;
            var font = _menuStyle.Font ?? _defaultFont!;

            using var _ = UI.BeginPopup(WidgetIds.Menu + level, style);

            if (UI.IsClosed())
                shouldClose = true;

            // Menu container — use ContainerStyle so each has its own BeginTree/EndTree.
            // This is critical for submenus: the recursive MenuUI call creates a popup
            // inside the column, and separate trees prevent EndTree from interfering.
            var menuContainer = new ContainerStyle()
            {
                Size = Size2.Fit,
                Background = _menuStyle.BackgroundColor,
                BorderRadius = _menuStyle.BorderRadius,
                BorderWidth = _menuStyle.BorderWidth,
                BorderColor = _menuStyle.BorderColor,
                Padding = _menuStyle.Padding,
            };

            using var __ = UI.BeginContainer(menuContainer);
            using var ___ = UI.BeginColumn(ContainerStyle.Fit);

            // Title
            if (level == 0 && _title != null)
            {
                ElementTree.BeginTree();
                ElementTree.BeginPadding(_menuStyle.TitlePadding);
                ElementTree.Text(_title, font, _menuStyle.TitleFontSize, _menuStyle.TitleColor, new Align2(Align.Min, Align.Center));
                ElementTree.EndTree();

                SeparatorUI();
            }

            // Items
            for (var index = startIndex; index < _itemCount; index++)
            {
                ref var item = ref _items![index];
                if (item.Level <= parentLevel) break;
                if (item.Level != parentLevel + 1) continue;

                if (item.Label == null)
                {
                    SeparatorUI();
                    continue;
                }

                var hasChildren = HasChildren(index);
                var isSubmenuOpen = level < MaxSubmenuDepth && _levels[level].OpenSubmenu == index;
                var itemId = WidgetIds.Item + index;
                var enabled = item.IsEnabled;

                if (hasChildren)
                {
                    SubmenuItemUI(level, index, itemId, ref item, enabled, isSubmenuOpen, ref executed, ref shouldClose);
                    continue;
                }

                if (ItemUI(itemId, ref item, enabled, _levels[level].ShowChecked, _levels[level].ShowIcons))
                    executed = item.Handler;

                if (UI.IsHovered(itemId) && _levels[level].OpenSubmenu >= 0 && UpdateSubmenuHover(level, -1))
                {
                    _levels[level].OpenSubmenu = -1;
                    for (var l = level + 1; l < MaxSubmenuDepth; l++)
                        _levels[l].OpenSubmenu = -1;
                }
            }
        }

        private static void SubmenuItemUI(int level, int index, WidgetId itemId, ref PopupMenuItem item, bool enabled, bool isSubmenuOpen, ref Action? executed, ref bool shouldClose)
        {
            var font = _menuStyle.Font ?? _defaultFont!;

            // Render submenu item (like a regular item but with arrow instead of shortcut)
            ElementTree.BeginTree();
            ElementTree.BeginWidget(itemId);

            var flags = ElementTree.GetWidgetFlags();
            var hovered = flags.HasFlag(WidgetFlags.Hovered) && enabled;
            var textColor = enabled ? _menuStyle.TextColor : _menuStyle.DisabledTextColor;

            ElementTree.BeginSize(Size.Default, new Size(_menuStyle.ItemHeight));
            ElementTree.BeginFill(hovered ? _menuStyle.ItemHoverColor : Color.Transparent);
            ElementTree.BeginPadding(_menuStyle.ItemPadding);
            ElementTree.BeginRow(_menuStyle.ItemContentSpacing);

            if (_levels[level].ShowChecked)
            {
                if (item.IsChecked && _menuStyle.CheckIcon != null)
                    ElementTree.Image(_menuStyle.CheckIcon, new Size2(_menuStyle.CheckWidth, Size.Default), ImageStretch.Uniform, textColor, align: new Align2(Align.Center, Align.Center));
                else
                    ElementTree.Spacer(_menuStyle.CheckWidth);
            }

            if (_levels[level].ShowIcons)
            {
                if (item.Icon != null)
                    ElementTree.Image(item.Icon, new Size2(_menuStyle.IconSize, _menuStyle.IconSize), ImageStretch.Uniform, textColor, align: new Align2(Align.Center, Align.Center));
                else
                    ElementTree.Spacer(_menuStyle.IconSize);
            }

            ElementTree.Text(item.Label!, font, _menuStyle.FontSize, textColor, new Align2(Align.Min, Align.Center));

            ElementTree.Flex();

            if (_menuStyle.SubmenuIcon != null)
                ElementTree.Image(_menuStyle.SubmenuIcon, new Size2(_menuStyle.IconSize, _menuStyle.IconSize), ImageStretch.Uniform, textColor, align: new Align2(Align.Center, Align.Center));

            ElementTree.EndTree();

            if (flags.HasFlag(WidgetFlags.Pressed) && enabled && !isSubmenuOpen)
            {
                _levels[level].OpenSubmenu = index;

                for (var l = level + 1; l < MaxSubmenuDepth; l++)
                    _levels[l].OpenSubmenu = -1;
            }

            hovered = UI.IsHovered(itemId);
            if (hovered && _levels[level].OpenSubmenu != index)
            {
                var targetIndex = enabled ? index : -1;
                if (UpdateSubmenuHover(level, targetIndex))
                {
                    _levels[level].OpenSubmenu = targetIndex >= 0 ? targetIndex : -1;
                    for (var l = level + 1; l < MaxSubmenuDepth; l++)
                        _levels[l].OpenSubmenu = -1;
                }
            }
            else if (hovered && _levels[level].OpenSubmenu == index)
            {
                ResetSubmenuTimer();
            }

            if (isSubmenuOpen)
            {
                _levels[level + 1].ShowChecked = item.ShowChecked;
                _levels[level + 1].ShowIcons = item.ShowIcons;
                var itemRect = UI.GetElementWorldRect(itemId);

                var popupStyle = new PopupStyle
                {
                    AnchorX = Align.Max,
                    AnchorY = Align.Min,
                    PopupAlignX = Align.Min,
                    PopupAlignY = Align.Min,
                    Spacing = _menuStyle.SubmenuSpacing,
                    ClampToScreen = true,
                    AnchorRect = itemRect
                };

                MenuUI(level + 1, index, popupStyle, ref executed, ref shouldClose);
            }
        }
    }

    public static void OpenPopupMenu(
        WidgetId id,
        ReadOnlySpan<PopupMenuItem> items,
        in PopupMenuStyle style,
        string? title = null)
        => PopupHelper.Open(id, items, style, title);

    public static void OpenPopupMenu(
        WidgetId id,
        ReadOnlySpan<PopupMenuItem> items,
        in PopupMenuStyle style,
        PopupStyle popupStyle,
        string? title = null)
        => PopupHelper.Open(id, items, style, popupStyle, title);

    public static bool IsPopupMenuOpen(WidgetId id) => PopupHelper.IsOpen(id);
    public static void ClosePopupMenu() => PopupHelper.Close();
}
