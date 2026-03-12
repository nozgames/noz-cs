//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using CrypticWizard.RandomWordGenerator;

namespace NoZ.Editor;

public partial class GenSpriteEditor : DocumentEditor, IShapeEditorHost
{
    private static partial class WidgetIds
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId LayerItem { get; }
        public static partial WidgetId GenerateButton { get; }
        public static partial WidgetId ConstraintDropDown { get; }
        public static partial WidgetId StyleDropDown { get; }
        public static partial WidgetId LayerPrompt { get; }
        public static partial WidgetId LayerNegativePrompt { get; }
        public static partial WidgetId LayerSeed { get; }
        public static partial WidgetId LayerSeedDice { get; }
        public static partial WidgetId LayerDeleteButton { get; }
        public static partial WidgetId LayerMoveUp { get; }
        public static partial WidgetId LayerMoveDown { get; }
        public static partial WidgetId PathNormal { get; }
        public static partial WidgetId PathSubtract { get; }
        public static partial WidgetId PathClip { get; }
        public static partial WidgetId FillColor { get; }
        public static partial WidgetId StrokeColor { get; }
        public static partial WidgetId AddComponentButton { get; }
        public static partial WidgetId AddComponentPopup { get; }
        public static partial WidgetId CancelButton { get; }
    }

    private static readonly WordGenerator _wordGenerator = new();

    private readonly ShapeEditor _shapeEditor;

    public new GenSpriteDocument Document => (GenSpriteDocument)base.Document;

    public override bool ShowInspector => true;

    private Shape CurrentShape => Document.ActiveLayer.Shape;

    public GenSpriteEditor(GenSpriteDocument doc) : base(doc)
    {
        _shapeEditor = new ShapeEditor(this);

        Commands =
        [
            .._shapeEditor.GetCommands(),
            new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab },
            new Command { Name = "Generate", Handler = () => Document.GenerateAsync(), Key = InputCode.KeyG, Ctrl = true },
            new Command { Name = "Eye Dropper", Handler = BeginEyeDropper, Key = InputCode.KeyI },
        ];
    }

    public override void OnUndoRedo()
    {
        Document.UpdateBounds();
    }

    public override void Update()
    {
        _shapeEditor.HandleDeleteKey();

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(6);
            Document.DrawOrigin();
            Graphics.SetSortGroup(5);
            DrawAllLayerWireframes();
        }

        UpdateMesh();
        DrawGeneratedImage(sortGroup: 1, alpha: 1f);
        DrawColoredMesh(sortGroup: 2);
        DrawGeneratedImage(sortGroup: 3, alpha: 0.3f);
    }

    public override void LateUpdate()
    {
        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
            _shapeEditor.HandleDragStart();
        else if (Input.WasButtonReleased(InputCode.MouseLeft))
            HandleLeftClick();
    }

    public override void UpdateUI() { }

    public override void InspectorUI()
    {
        if (_shapeEditor.HasPathSelection)
            PathInspectorUI();
        else
            GenSpriteInspectorUI();

        LayersInspectorUI();

        AddComponentUI();

        UI.Flex();

        var genImage = Document.Generation;
        if (genImage.IsGenerating)
            ProgressUI(genImage);
        else
            GenerateButtonUI(genImage);
    }

    public override void Dispose()
    {
        _shapeEditor.ClearSelection();
        EditorUI.ClosePopup();
        base.Dispose();
    }

    #region Inspector

    private void ConstraintUI()
    {
        var sizes = EditorApplication.Config.SpriteSizes;
        var constraintLabel = "256x256";
        for (int i = 0; i < sizes.Length; i++)
            if (Document.ConstrainedSize == sizes[i].Size)
            {
                constraintLabel = sizes[i].Label;
                break;
            }

        UI.DropDown(WidgetIds.ConstraintDropDown, () => sizes.Select(s =>
            PopupMenuItem.Item(s.Label, () => SetConstraint(s.Size))).ToArray(), constraintLabel, EditorAssets.Sprites.IconConstraint);
    }

    private void StyleUI()
    {
        UI.DropDown(WidgetIds.StyleDropDown, () =>
        {
            var items = new List<PopupMenuItem>
            {
                PopupMenuItem.Item("None", () => SetStyle(null))
            };
            foreach (var doc in DocumentManager.Documents)
            {
                if (doc is GenStyleDocument styleDoc)
                    items.Add(PopupMenuItem.Item(styleDoc.Name, () => SetStyle(styleDoc)));
            }
            return [.. items];
        }, Document.StyleName ?? "None",
        icon: EditorAssets.Sprites.AssetIconGenstyle);
    }

    private void GenSpriteInspectorUI()
    {
        if (_shapeEditor.HasPathSelection)
            return;

        using var _ = Inspector.BeginSection("GENSPRITE");
        if (Inspector.IsSectionCollapsed) return;

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            ConstraintUI();

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            StyleUI();
    }

    private void SetStyle(GenStyleDocument? style)
    {
        Undo.Record(Document);
        Document.StyleName = style?.Name;
        Document.Style = style;
        Document.SaveMetadata();
        Document.IncrementVersion();
    }

    private void SetConstraint(Vector2Int size)
    {
        Undo.Record(Document);
        Document.ConstrainedSize = size;
        Document.UpdateBounds();
        Document.SaveMetadata();
        Document.IncrementVersion();
    }

    private void AddComponentUI()
    {
        using (UI.BeginContainer(new ContainerStyle { Padding = EdgeInsets.Symmetric(10, 16), AlignX = Align.Center }))
        {
            if (UI.Button(WidgetIds.AddComponentButton, () =>
            {
                UI.Text("+ Add Layer", EditorStyle.Control.Text);
            }, EditorStyle.Button.Secondary))
            {
                Undo.Record(Document);
                Document.AddLayer();
                _shapeEditor.ClearSelection();
                Document.IncrementVersion();
            }
        }
    }

    private void ProgressUI(GenerationImage genImage)
    {
        using (UI.BeginColumn(new ContainerStyle
        {
            Padding = EdgeInsets.Symmetric(12, 16),
            Spacing = 10,
        }))
        {
            var progressText = genImage.GenerationState switch
            {
                GenerationState.Queued when genImage.QueuePosition > 0 =>
                    $"Queued (position {genImage.QueuePosition})",
                GenerationState.Queued => "Queued...",
                GenerationState.Running => $"Generating {(int)(genImage.GenerationProgress * 100)}%",
                _ => "Starting..."
            };

            using (UI.BeginRow(new ContainerStyle { Spacing = 8 }))
            {
                UI.Text(progressText, EditorStyle.Text.Primary with { FontSize = EditorStyle.Control.TextSize});
                UI.Flex();
                if (UI.Button(WidgetIds.CancelButton, EditorAssets.Sprites.IconClose, EditorStyle.Button.SmallIconOnly))
                    genImage.CancelGeneration();
            }

            using (UI.BeginContainer(new ContainerStyle
            {
                Width = Size.Percent(1),
                Height = 4f,
                Color = EditorStyle.Palette.Active,
                BorderRadius = 2f
            }))
            {
                UI.Container(new ContainerStyle
                {
                    Width = Size.Percent(genImage.GenerationProgress),
                    Height = 4f,
                    Color = EditorStyle.Palette.Primary,
                    BorderRadius = 2f
                });
            }
        }
    }

    private void GenerateButtonUI(GenerationImage genImage)
    {
        if (genImage.GenerationError != null)
            UI.Text(genImage.GenerationError, EditorStyle.Text.Secondary with { Color = EditorStyle.ErrorColor });

        using (UI.BeginContainer(new ContainerStyle
        {
            Padding = EdgeInsets.Symmetric(12, 16),
        }))
        {
            if (UI.Button(WidgetIds.GenerateButton, "Generate", EditorAssets.Sprites.IconAi, EditorStyle.Button.Primary with { Width = Size.Percent(1), MinWidth = 0, Height = 36 }))
                Document.GenerateAsync();
        }
    }

    private void LayersInspectorUI()
    {
        for (int i = Document.Layers.Count - 1; i >= 0; i--)
        {
            var layer = Document.Layers[i];
            var isActive = Document.ActiveLayerIndex == i;
            var layerIndex = i;

            var prompt = layer.Prompt;
            var title = string.IsNullOrEmpty(prompt) ? layer.Name
                : prompt.Length > 28 ? prompt[..28] + "..."
                : prompt;

            void LayerHeaderContent()
            {
                if (layerIndex < Document.Layers.Count - 1)
                {
                    if (UI.Button(WidgetIds.LayerMoveUp + layerIndex, EditorAssets.Sprites.IconExpandUp, EditorStyle.Button.SmallIconOnly))
                    {
                        Undo.Record(Document);
                        Document.MoveLayer(layerIndex, layerIndex + 1);
                        Document.ActiveLayerIndex = layerIndex + 1;
                        Document.IncrementVersion();
                    }
                }

                if (layerIndex > 0)
                {
                    if (UI.Button(WidgetIds.LayerMoveDown + layerIndex, EditorAssets.Sprites.IconExpandDown, EditorStyle.Button.SmallIconOnly))
                    {
                        Undo.Record(Document);
                        Document.MoveLayer(layerIndex, layerIndex - 1);
                        Document.ActiveLayerIndex = layerIndex - 1;
                        Document.IncrementVersion();
                    }
                }

                if (Document.Layers.Count > 1)
                {
                    if (UI.Button(WidgetIds.LayerDeleteButton + layerIndex, EditorAssets.Sprites.IconDelete, EditorStyle.Button.SmallIconOnly))
                    {
                        Undo.Record(Document);
                        Document.RemoveLayer(layerIndex);
                        _shapeEditor.ClearSelection();
                        Document.IncrementVersion();
                    }
                }
            }

            using (Inspector.BeginSection(title, icon: EditorAssets.Sprites.IconLayer, isActive: isActive, content: LayerHeaderContent))
            {
                if (Inspector.WasHeaderPressed)
                {
                    _shapeEditor.ClearSelection();
                    Document.ActiveLayerIndex = layerIndex;

                    var allSelected = layer.Shape.AnchorCount > 0;
                    for (ushort a = 0; a < layer.Shape.AnchorCount; a++)
                    {
                        if (!layer.Shape.GetAnchor(a).IsSelected) { allSelected = false; break; }
                    }

                    if (!allSelected && layer.Shape.AnchorCount > 0)
                        layer.Shape.SelectAll();
                    else
                        layer.Shape.ClearSelection();

                    _shapeEditor.UpdateSelection();
                }

                if (!Inspector.IsSectionCollapsed)
                {
                    using (Inspector.BeginRow())
                    using (UI.BeginFlex())
                        layer.Prompt = UI.TextInput(WidgetIds.LayerPrompt + i, layer.Prompt, EditorStyle.TextArea, "Prompt", Document, multiLine: true);

                    using (Inspector.BeginRow())
                    using (UI.BeginFlex())
                        layer.NegativePrompt = UI.TextInput(WidgetIds.LayerNegativePrompt + i, layer.NegativePrompt, EditorStyle.TextArea, "Negative Prompt", Document, multiLine: true);

                    using (Inspector.BeginRow())
                    {
                        using (UI.BeginFlex())
                            layer.Seed = UI.TextInput(WidgetIds.LayerSeed + i, layer.Seed, EditorStyle.TextInput, "Seed", Document, icon: EditorAssets.Sprites.IconSeed);
                        if (UI.Button(WidgetIds.LayerSeedDice + i, EditorAssets.Sprites.IconRandom, EditorStyle.Button.IconOnly))
                        {
                            Undo.Record(Document);
                            layer.Seed = GenerateRandomSeed();
                        }
                    }


                }
            }
        }
    }

    private void PathInspectorUI()
    {
        using (Inspector.BeginSection("PATH"))
        {
            if (!Inspector.IsSectionCollapsed)
            {
                using (Inspector.BeginRow())
                {
                    var shape = CurrentShape;
                    var currentOp = ShapeEditor.GetSelectedPathOperation(shape);

                    PathToggle(WidgetIds.PathNormal, EditorAssets.Sprites.IconFill, "Fill", currentOp == PathOperation.Normal, PathOperation.Normal);
                    PathToggle(WidgetIds.PathSubtract, EditorAssets.Sprites.IconSubtract, "Sub", currentOp == PathOperation.Subtract, PathOperation.Subtract);
                    PathToggle(WidgetIds.PathClip, EditorAssets.Sprites.IconClip, "Clip", currentOp == PathOperation.Clip, PathOperation.Clip);
                }
            }
        }

        using (Inspector.BeginSection("FILL"))
        {
            if (!Inspector.IsSectionCollapsed)
            {
                using (Inspector.BeginRow())
                {
                    using var __ = UI.BeginFlex();
                    var fillColor = Document.CurrentFillColor;
                    if (EditorUI.ColorButton(WidgetIds.FillColor, ref fillColor, EditorStyle.Inspector.ColorButton))
                    {
                        UI.HandleChange(Document);
                        SetFillColor(fillColor);
                    }
                }
            }
        }

        using (Inspector.BeginSection("STROKE"))
        {
            if (!Inspector.IsSectionCollapsed)
            {
                using (Inspector.BeginRow())
                {
                    using var __ = UI.BeginFlex();
                    var strokeColor = Document.CurrentStrokeColor;
                    if (EditorUI.ColorButton(WidgetIds.StrokeColor, ref strokeColor, EditorStyle.Inspector.ColorButton))
                    {
                        UI.HandleChange(Document);
                        SetStrokeColor(strokeColor);
                    }
                }
            }
        }
    }

    private void PathToggle(WidgetId id, Sprite icon, string label, bool isChecked, PathOperation op)
    {
        var hovered = UI.IsHovered(id);
        var textColor = isChecked ? EditorStyle.Palette.Content : EditorStyle.Palette.Label;

        using (UI.BeginContainer(id, new ContainerStyle
        {
            Width = Size.Percent(1),
            Height = 32,
            BorderRadius = 4,
            Color = isChecked ? EditorStyle.Palette.Active : (hovered ? EditorStyle.Palette.Header : EditorStyle.Palette.Header),
            AlignX = Align.Center,
            AlignY = Align.Center,
            Spacing = 6,
        }))
        {
            UI.Image(icon, new ImageStyle
            {
                Size = 14,
                Color = textColor,
                Align = Align.Center,
            });
            UI.Text(label, new LabelStyle
            {
                FontSize = EditorStyle.Inspector.FontSize,
                Color = textColor,
                AlignY = Align.Center,
            });

            if (UI.WasPressed())
                _shapeEditor.SetPathOperation(op);
        }
    }

    private void SetFillColor(Color32 color)
    {
        Document.CurrentFillColor = color;

        var shape = CurrentShape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathFillColor(p, color);
            _meshVersion = -1;
        }
    }

    private void SetStrokeColor(Color32 color)
    {
        Document.CurrentStrokeColor = color;

        var shape = CurrentShape;
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;
            shape.SetPathStroke(p, color, path.StrokeWidth);
            _meshVersion = -1;
        }
    }

    private void BeginEyeDropper() => Workspace.BeginTool(new EyeDropperTool(this));

    internal void ApplyEyeDropperColor(Color32 color, bool shift)
    {
        if (shift)
        {
            SetStrokeColor(color);
            if (ColorPicker.IsOpen(WidgetIds.StrokeColor))
                ColorPicker.Open(WidgetIds.StrokeColor, color);
        }
        else
        {
            SetFillColor(color);
            if (ColorPicker.IsOpen(WidgetIds.FillColor))
                ColorPicker.Open(WidgetIds.FillColor, color);
        }
    }

    #endregion

    #region Input

    private void HandleLeftClick()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        var shift = Input.IsShiftDown(InputScope.All);

        // First pass: check all layers for anchor/segment hits (priority over path containment)
        for (int layerIdx = Document.Layers.Count - 1; layerIdx >= 0; layerIdx--)
        {
            var layer = Document.Layers[layerIdx];
            var shape = layer.Shape;
            var result = shape.HitTest(localMousePos);

            if (result.AnchorIndex != ushort.MaxValue)
            {
                _shapeEditor.SelectAnchor(shape, result.AnchorIndex, shift);
                return;
            }

            if (result.SegmentIndex != ushort.MaxValue)
            {
                _shapeEditor.SelectSegment(shape, result.SegmentIndex, shift);
                return;
            }
        }

        // Second pass: check for path containment (selects layer)
        for (int layerIdx = Document.Layers.Count - 1; layerIdx >= 0; layerIdx--)
        {
            var layer = Document.Layers[layerIdx];
            var shape = layer.Shape;
            var result = shape.HitTest(localMousePos);

            if (result.PathIndex != ushort.MaxValue)
            {
                Document.ActiveLayerIndex = layerIdx;
                _shapeEditor.SelectPath(shape, result.PathIndex, shift);
                return;
            }
        }

        if (!shift)
            _shapeEditor.ClearSelection();
    }

    #endregion

    #region Drawing

    private void DrawAllLayerWireframes()
    {
        var layers = Document.Layers;

        // Draw non-active layers (dimmed segments + selected anchors only)
        for (int layerIdx = 0; layerIdx < layers.Count; layerIdx++)
        {
            if (layerIdx == Document.ActiveLayerIndex) continue;
            var shape = layers[layerIdx].Shape;
            ShapeEditor.DrawSegments(shape, dimmed: true);
            ShapeEditor.DrawAnchors(shape, selectedOnly: true);
        }

        // Draw active layer on top (full brightness + all anchors)
        var activeShape = CurrentShape;
        ShapeEditor.DrawSegments(activeShape, dimmed: false);
        ShapeEditor.DrawAnchors(activeShape);
    }

    #endregion

    #region IShapeEditorHost

    Document IShapeEditorHost.Document => base.Document;
    Shape IShapeEditorHost.CurrentShape => CurrentShape;
    Color32 IShapeEditorHost.NewPathFillColor => Document.CurrentFillColor;
    PathOperation IShapeEditorHost.NewPathOperation => PathOperation.Normal;
    bool IShapeEditorHost.SnapToPixelGrid => false;

    void IShapeEditorHost.OnSelectionChanged(bool hasSelection)
    {
        foreach (var layer in Document.Layers)
        {
            var shape = layer.Shape;
            for (ushort p = (ushort)(shape.PathCount - 1); p < shape.PathCount; p--)
            {
                ref readonly var path = ref shape.GetPath(p);
                if (!path.IsSelected) continue;

                Document.CurrentFillColor = path.FillColor;
                Document.CurrentStrokeColor = path.StrokeColor;
                return;
            }
        }
    }

    void IShapeEditorHost.ClearAllSelections()
    {
        foreach (var layer in Document.Layers)
            layer.Shape.ClearSelection();
    }

    void IShapeEditorHost.InvalidateMesh() => _meshVersion = -1;

    private static string GenerateRandomSeed()
    {
        var adj = _wordGenerator.GetWord(WordGenerator.PartOfSpeech.adj);
        var noun = _wordGenerator.GetWord(WordGenerator.PartOfSpeech.noun);
        return $"{adj}-{noun}";
    }

    Shape? IShapeEditorHost.GetShapeWithSelection()
    {
        foreach (var layer in Document.Layers)
            if (layer.Shape.HasSelection()) return layer.Shape;
        return null;
    }

    void IShapeEditorHost.ForEachEditableShape(Action<Shape> action)
    {
        foreach (var layer in Document.Layers)
            action(layer.Shape);
    }

    #endregion
}
