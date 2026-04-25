//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class Inspector
{
    public struct AutoSection : IDisposable { readonly void IDisposable.Dispose() => EndSection(); }
    public struct AutoProperty : IDisposable { readonly void IDisposable.Dispose() => EndProperty(); }

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
        public static partial WidgetId DocumentName { get; }
        public static partial WidgetId DocumentExport { get; }
        public static partial WidgetId DocumentType { get; }
    }

    private static WidgetId _nextSectionId;
    private static bool _sectionCollapsed;
    private static bool _sectionActive;
    private static bool _wasHeaderPressed;
    private static bool _sectionOpen;

    public static bool IsSectionCollapsed => _sectionCollapsed;
    public static bool WasHeaderPressed => _wasHeaderPressed;

    public static void UpdateUI()
    {
        _nextSectionId = ElementId.Section;
        _sectionOpen = false;

        using (UI.BeginRow(ElementId.Root, EditorStyle.Inspector.Root))
        {
            using (UI.BeginFlex())
            using (UI.BeginScrollable(ElementId.Scroll))
            using (UI.BeginCursor(new SpriteCursor(EditorAssets.Sprites.CursorArrow)))
            using (UI.BeginColumn(new ContainerStyle { Spacing = EditorStyle.Control.Spacing }))
            {
                if (Workspace.ActiveEditor?.ShowInspector ?? false)
                    Workspace.ActiveEditor.InspectorUI();
                else if (Workspace.State == WorkspaceState.Default && Workspace.SelectedCount == 1)
                    DocumentInspectorUI(Workspace.GetFirstSelected()!);

                Finish();
            }

            UI.ScrollBar(ElementId.ScrollBar, ElementId.Scroll, EditorStyle.Inspector.ScrollBar);
        }
    }

    public static void Section(
        string name,
        Sprite? icon = null,
        Action? content = null,
        bool isActive = false,
        bool collapsed = false,
        bool empty = false)
    {
        if (_sectionOpen)
            EndSection();

        BeginSectionCore(name, icon, content, isActive, collapsed, empty);
        _sectionOpen = true;
    }

    public static AutoSection BeginSection(
        string name,
        Sprite? icon = null,
        Action? content = null,
        bool isActive = false,
        bool collapsed = false,
        bool empty = false)
    {
        BeginSectionCore(name, icon, content, isActive, collapsed, empty);
        return new AutoSection();
    }

    private static void BeginSectionCore(
        string name,
        Sprite? icon,
        Action? content,
        bool isActive,
        bool collapsed,
        bool empty)
    {
        var sectionId = _nextSectionId++;
        _sectionActive = isActive;

        ElementTree.BeginColumn();

        // header
        ElementTree.BeginTree();
        ref var state = ref ElementTree.BeginWidget<SectionState>(sectionId);
        var flags = ElementTree.GetWidgetFlags();
        var hovered = flags.HasFlag(WidgetFlags.Hovered);

        _wasHeaderPressed = flags.HasFlag(WidgetFlags.Pressed);
        _sectionCollapsed = empty || state.Collapsed != 0 || collapsed;

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
        if (empty)
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
            ElementTree.BeginFill(EditorStyle.Palette.Panel);
            ElementTree.BeginPadding(new EdgeInsets(
                EditorStyle.Control.Spacing,
                EditorStyle.Control.Spacing,
                0,
                EditorStyle.Control.Spacing));
            ElementTree.BeginColumn(EditorStyle.Inspector.BodyGap);            
        }
    }

    public static UI.AutoRow BeginRow()
    {
        return UI.BeginRow(EditorStyle.Inspector.Row);
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
            ElementTree.EndFill();
        }

        _sectionCollapsed = false;

        ElementTree.EndColumn();

        _sectionActive = false;
        _sectionOpen = false;
    }

    public static AutoProperty BeginProperty(string name)
    {
        ElementTree.BeginSize(Size.Default, Size.Fit, 0, float.MaxValue, EditorStyle.Control.Height, float.MaxValue);
        ElementTree.BeginRow();
        ElementTree.BeginFlex(0.4f);
        UI.Text(name, style: EditorStyle.Text.Secondary);
        ElementTree.EndFlex();
        ElementTree.BeginFlex(0.6f);
        ElementTree.BeginSize(Size.Default, Size.Fit, 0, float.MaxValue, EditorStyle.Control.Height, float.MaxValue);
        return new AutoProperty();
    }

    public static void EndProperty()
    {
        ElementTree.EndSize();
        ElementTree.EndFlex();
        ElementTree.EndRow();
        ElementTree.EndSize();
    }

    private static void DocumentInspectorUI(Document doc)
    {
        Section(doc.Def.Name.ToUpperInvariant(), doc.Def.Icon?.Invoke());
        if (!IsSectionCollapsed)
        {
            var ext = Path.GetExtension(doc.Path);
            var defs = Project.GetDefs(ext);
            if (defs != null && defs.Count > 1)
            {
                using (BeginProperty("Type"))
                {
                    UI.DropDown(ElementId.DocumentType, () =>
                        defs.Select(d => new PopupMenuItem
                        {
                            Label = d.Name,
                            Handler = () => Project.ChangeType(doc, d)
                        }).ToArray(),
                        doc.Def.Name, doc.Def.Icon?.Invoke());
                }
            }

            using (BeginProperty("Name"))
            {
                var newName = UI.TextInput(ElementId.DocumentName, doc.Name, EditorStyle.TextInput);
                if (newName != doc.Name)
                    Project.Rename(doc, newName);
            }

            if (doc.CanExport)
            {
                using (BeginProperty("Export"))
                {
                    var shouldExport = doc.ShouldExport;
                    if (UI.Toggle(ElementId.DocumentExport, shouldExport, EditorStyle.Inspector.Toggle))
                    {
                        Undo.Record(doc);
                        doc.ShouldExport = !shouldExport;
                        AssetManifest.IsModified = true;
                    }
                }
            }
        }

        doc.InspectorUI();
    }
}
