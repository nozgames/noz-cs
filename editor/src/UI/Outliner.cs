//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class Outliner
{
    public struct AutoSection : IDisposable { readonly void IDisposable.Dispose() => EndSection(); }

    private struct SectionState
    {
        public byte Collapsed;
    }

    private static partial class ElementId
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId Scroll { get; }
        public static partial WidgetId ScrollBar { get; }
        public static partial WidgetId Section { get; }
    }

    private static WidgetId _nextSectionId;
    private static bool _sectionCollapsed;
    private static bool _sectionActive;
    private static bool _wasHeaderPressed;
    private static bool _sectionOpen;

    public static bool IsSectionCollapsed => _sectionCollapsed;
    public static bool WasHeaderPressed => _wasHeaderPressed;

    public static bool UpdateUI()
    {
        if (!(Workspace.ActiveEditor?.ShowOutliner ?? Workspace.ShowProject))
            return false;

        _nextSectionId = ElementId.Section;
        _sectionOpen = false;

        using (UI.BeginRow(ElementId.Root, EditorStyle.Inspector.Root))
        {
            using (UI.BeginFlex())
            using (UI.BeginCursor(new SpriteCursor(EditorAssets.Sprites.CursorArrow)))            
            using (UI.BeginScrollable(ElementId.Scroll))
            using (UI.BeginColumn(new ContainerStyle { Spacing = EditorStyle.Control.Spacing }))
            {
                if (Workspace.ActiveEditor?.ShowOutliner ?? false)
                    Workspace.ActiveEditor.OutlinerUI();
                else
                    Workspace.ProjectViewUI();

                Finish();
            }

            UI.ScrollBar(ElementId.ScrollBar, ElementId.Scroll, EditorStyle.Inspector.ScrollBar);
        }

        return true;
    }

    public static AutoSection BeginSection(
        string name,
        Sprite? icon = null,
        Action? content = null,
        bool isActive = false,
        bool collapsed = false,
        bool empty = false,
        bool collapsible = true)
    {
        BeginSectionCore(name, icon, content, isActive, collapsed, empty, collapsible);
        return new AutoSection();
    }

    public static void Section(
        string name,
        Sprite? icon = null,
        Action? content = null,
        bool isActive = false,
        bool collapsed = false,
        bool empty = false,
        bool collapsible = true)
    {
        if (_sectionOpen)
            EndSection();

        BeginSectionCore(name, icon, content, isActive, collapsed, empty, collapsible);
        _sectionOpen = true;
    }

    private static void BeginSectionCore(
        string name,
        Sprite? icon,
        Action? content,
        bool isActive,
        bool collapsed,
        bool empty,
        bool collapsible)
    {
        var sectionId = _nextSectionId++;
        _sectionActive = isActive;

        ElementTree.BeginColumn();

        // header
        ElementTree.BeginTree();
        ref var state = ref ElementTree.BeginWidget<SectionState>(sectionId);
        var flags = ElementTree.GetWidgetFlags();
        var hovered = collapsible && flags.HasFlag(WidgetFlags.Hovered);

        _wasHeaderPressed = collapsible && flags.HasFlag(WidgetFlags.Pressed);
        _sectionCollapsed = collapsible && (empty || state.Collapsed != 0 || collapsed);

        if (_wasHeaderPressed)
            state.Collapsed = (byte)(state.Collapsed != 0 ? 0 : 1);

        var iconColor = isActive ? EditorStyle.Palette.Content : EditorStyle.Palette.SecondaryText;
        var headerBg = hovered ? EditorStyle.Palette.Active : EditorStyle.Palette.Separator;
        var chevron = _sectionCollapsed
            ? EditorAssets.Sprites.IconFoldoutOpen
            : EditorAssets.Sprites.IconFoldoutClosed;

        ElementTree.BeginSize(Size.Default, EditorStyle.Control.Height);
        ElementTree.BeginFill(headerBg, radius: BorderRadius.Only(
            topLeft: EditorStyle.Control.BorderRadius,
            topRight: EditorStyle.Control.BorderRadius,
            bottomLeft: _sectionCollapsed ? EditorStyle.Control.BorderRadius : 0,
            bottomRight: _sectionCollapsed ? EditorStyle.Control.BorderRadius : 0));
        ElementTree.BeginPadding(EdgeInsets.LeftRight(8));

        ElementTree.BeginRow(EditorStyle.Control.Spacing);
        if (!collapsible)
        {
            // no chevron, no spacer — text aligns to left padding
        }
        else if (empty)
            ElementTree.Spacer(EditorStyle.Icon.Size);
        else
            ElementTree.Image(chevron, EditorStyle.Icon.Size, ImageStretch.Uniform, iconColor, 1.0f, Align.Center);

        if (icon != null)
            ElementTree.Image(icon, EditorStyle.Control.IconSize, ImageStretch.Uniform, iconColor, 1.0f, new Align2(Align.Center, Align.Center));

        ElementTree.Text(name, UI.DefaultFont, EditorStyle.Control.TextSize, EditorStyle.Palette.SecondaryText, new Align2(Align.Min, Align.Center));
        ElementTree.Flex();

        content?.Invoke();

        ElementTree.EndTree();

        // content

        if (!_sectionCollapsed)
        {
            ElementTree.BeginPadding(new EdgeInsets(
                EditorStyle.Control.Spacing,
                EditorStyle.Control.Spacing,
                0,
                EditorStyle.Control.Spacing));
            ElementTree.BeginColumn(EditorStyle.Inspector.BodyGap);
        }
    }

    public static void Finish()
    {
        if (_sectionOpen)
        {
            EndSection();
            _sectionOpen = false;
        }
    }

    public static void EndSection()
    {
        if (!_sectionCollapsed)
        {
            ElementTree.EndColumn();
            ElementTree.EndPadding();
        }

        _sectionCollapsed = false;

        ElementTree.EndColumn();

        _sectionActive = false;
        _sectionOpen = false;
    }
}
