//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class SceneEditor : DocumentEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId AddButton { get; }
        public static partial WidgetId InspectorToggle { get; }
        public static partial WidgetId LayerToggle { get; }
        public static partial WidgetId ExitEditMode { get; }
        public static partial WidgetId ContextMenu { get; }
        public static partial WidgetId InspectorName { get; }
        public static partial WidgetId InspectorPosX { get; }
        public static partial WidgetId InspectorPosY { get; }
        public static partial WidgetId InspectorRotation { get; }
        public static partial WidgetId InspectorScaleX { get; }
        public static partial WidgetId InspectorScaleY { get; }
        public static partial WidgetId InspectorColor { get; }
        public static partial WidgetId InspectorSprite { get; }
        public static partial WidgetId InspectorPlaceholder { get; }
    }

    public new SceneDocument Document => (SceneDocument)base.Document;

    private bool _showOutliner = true;
    private bool _showInspector = true;

    public override bool ShowOutliner => _showOutliner;
    public override bool ShowInspector => _showInspector;

    public SceneEditor(SceneDocument doc) : base(doc)
    {
        Commands =
        [
            new Command("Exit Edit Mode",   Workspace.EndEdit,      [InputCode.KeyTab]),
            new Command("Delete",           DeleteSelected,         [InputCode.KeyX, InputCode.KeyDelete]),
            new Command("Duplicate",        DuplicateSelected,      [new KeyBinding(InputCode.KeyD, ctrl: true)]),
            new Command("Copy",             CopySelected,           [new KeyBinding(InputCode.KeyC, ctrl: true)]),
            new Command("Paste",            PasteSelected,          [new KeyBinding(InputCode.KeyV, ctrl: true)]),
            new Command("Cut",              CutSelected,            [new KeyBinding(InputCode.KeyX, ctrl: true)]),
            new Command("Select All",       SelectAll,              [new KeyBinding(InputCode.KeyA, ctrl: true)]),
            new Command("Group",             GroupSelected,         [new KeyBinding(InputCode.KeyG, ctrl: true)]),
            new Command("Frame Selection",  FrameSelection,         [InputCode.KeyF]),
        ];

        SetMode(new SceneTransformMode());
    }

    public override void Update()
    {
        Mode?.Update();

        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.Document);
            Document.DrawNodeTree(Document.Root, Matrix3x2.Identity, Color.White);
        }

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(5);
            Document.DrawOrigin();
            Graphics.SetSortGroup(4);
            DrawSelectionOverlay();
        }

        Document.DrawBounds();

        Mode?.Draw();
    }

    public override void UpdateOverlayUI() { }

    public override void OnUndoRedo()
    {
        base.OnUndoRedo();
        RebuildSelection();
    }

    public override void OpenContextMenu(WidgetId popupId)
    {
        var hasSel = SelectedNodes.Count > 0;
        var single = SelectedNodes.Count == 1;

        var items = new List<PopupMenuItem>
        {
            PopupMenuItem.Item("Cut",       CutSelected,        new KeyBinding(InputCode.KeyX, ctrl: true), enabled: () => hasSel),
            PopupMenuItem.Item("Copy",      CopySelected,       new KeyBinding(InputCode.KeyC, ctrl: true), enabled: () => hasSel),
            PopupMenuItem.Item("Paste",     PasteSelected,      new KeyBinding(InputCode.KeyV, ctrl: true), enabled: () => Clipboard.Get<SceneClipboardData>() != null),
            PopupMenuItem.Item("Duplicate", DuplicateSelected,  new KeyBinding(InputCode.KeyD, ctrl: true), enabled: () => hasSel),
            PopupMenuItem.Item("Delete",    DeleteSelected,     InputCode.KeyX,                            enabled: () => hasSel),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Select All", SelectAll, new KeyBinding(InputCode.KeyA, ctrl: true)),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Rename", () => { if (SelectedNodes.Count == 1) BeginRename(SelectedNodes[0]); }, InputCode.KeyF2, enabled: () => single),

            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Group", GroupSelected, new KeyBinding(InputCode.KeyG, ctrl: true), enabled: () => hasSel),
        };

        UI.OpenPopupMenu(WidgetIds.ContextMenu, items.ToArray(), EditorStyle.ContextMenu.Style);
    }

    public override void ToolbarUI()
    {
        base.ToolbarUI();

        if (UI.Button(WidgetIds.LayerToggle, EditorAssets.Sprites.IconLayer, EditorStyle.Button.ToggleIcon, isSelected: _showOutliner))
            _showOutliner = !_showOutliner;

        if (UI.Button(WidgetIds.ExitEditMode, EditorAssets.Sprites.IconEdit, EditorStyle.Button.ToggleIcon, isSelected: true))
            Workspace.EndEdit();

        UI.Flex();

        if (UI.Button(WidgetIds.InspectorToggle, EditorAssets.Sprites.IconInfo, EditorStyle.Button.ToggleIcon, isSelected: _showInspector))
            _showInspector = !_showInspector;
    }

    public override void InspectorUI()
    {
        if (SelectedNodes.Count != 1)
            return;

        var node = SelectedNodes[0];

        using (Inspector.BeginSection(node is SceneGroup ? "GROUP" : "SPRITE"))
        {
            if (Inspector.IsSectionCollapsed)
                return;

            using (Inspector.BeginProperty("Name"))
            {
                var newName = UI.TextInput(WidgetIds.InspectorName, node.Name, EditorStyle.TextInput);
                if (newName != node.Name)
                {
                    Undo.Record(Document);
                    node.Name = newName;
                }
            }

            if (node is SceneSprite sceneSprite)
            {
                using (Inspector.BeginProperty("Sprite"))
                {
                    var newRef = EditorUI.SpriteField(WidgetIds.InspectorSprite, sceneSprite.Sprite);
                    if (UI.WasChanged())
                    {
                        Undo.Record(Document);
                        sceneSprite.Sprite = newRef;
                    }
                }
            }

            using (Inspector.BeginProperty("Position"))
            using (UI.BeginRow(EditorStyle.Control.Spacing))
            {
                float x, y;
                using (UI.BeginFlex())
                    x = EditorUI.FloatInput(WidgetIds.InspectorPosX, node.Position.X, EditorStyle.TextInput, step: 0.1f, fineStep: 0.01f);
                using (UI.BeginFlex())
                    y = EditorUI.FloatInput(WidgetIds.InspectorPosY, node.Position.Y, EditorStyle.TextInput, step: 0.1f, fineStep: 0.01f);
                if (x != node.Position.X || y != node.Position.Y)
                {
                    Undo.Record(Document);
                    node.Position = new Vector2(x, y);
                }
            }

            using (Inspector.BeginProperty("Rotation"))
            {
                var deg = node.Rotation * 180f / MathF.PI;
                var newDeg = EditorUI.FloatInput(WidgetIds.InspectorRotation, deg, EditorStyle.TextInput, step: 1f, fineStep: 0.1f);
                if (newDeg != deg)
                {
                    Undo.Record(Document);
                    node.Rotation = newDeg * MathF.PI / 180f;
                }
            }

            using (Inspector.BeginProperty("Scale"))
            using (UI.BeginRow(EditorStyle.Control.Spacing))
            {
                float x, y;
                using (UI.BeginFlex())
                    x = EditorUI.FloatInput(WidgetIds.InspectorScaleX, node.Scale.X, EditorStyle.TextInput, step: 0.1f, fineStep: 0.01f);
                using (UI.BeginFlex())
                    y = EditorUI.FloatInput(WidgetIds.InspectorScaleY, node.Scale.Y, EditorStyle.TextInput, step: 0.1f, fineStep: 0.01f);
                if (x != node.Scale.X || y != node.Scale.Y)
                {
                    Undo.Record(Document);
                    node.Scale = new Vector2(x, y);
                }
            }

            using (Inspector.BeginProperty("Color"))
            {
                var newColor = EditorUI.ColorButton(WidgetIds.InspectorColor, node.Color.ToColor());
                if (UI.WasChangeStarted()) Undo.Record(Document);
                if (UI.WasChanged()) node.Color = newColor.ToColor32();
                if (UI.WasChangeCancelled()) Undo.Cancel();
            }

            if (node is SceneSprite)
            {
                using (Inspector.BeginProperty("Placeholder"))
                {
                    var p = UI.Toggle(WidgetIds.InspectorPlaceholder, node.Placeholder, EditorStyle.Inspector.Toggle);
                    if (UI.WasChanged())
                    {
                        Undo.Record(Document);
                        node.Placeholder = p;
                    }
                }
            }
        }
    }

    private void FrameSelection()
    {
        if (SelectedNodes.Count == 0)
        {
            Workspace.FrameRect(Document.Bounds.Translate(Document.Position));
            return;
        }

        var bounds = SelectionWorldBounds();
        if (bounds.Width > 0 || bounds.Height > 0)
            Workspace.FrameRect(bounds.Translate(Document.Position));
    }
}
