//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal partial class CompositeSoundEditor : DocumentEditor
{
    private static partial class ElementId
    {
        public static partial WidgetId PlayButton { get; }
        public static partial WidgetId LoopButton { get; }
        public static partial WidgetId OutlinerPanel { get; }
        public static partial WidgetId OutlinerSplitter { get; }
        public static partial WidgetId AddLayerButton { get; }
        public static partial WidgetId LayerRow { get; }
    }

    private const float WaveformHeight = 0.8f;
    private const float LayerSpacing = 0.15f;
    private const float UnselectedAlpha = 0.4f;
    private const float ActiveAlpha = 0.85f;

    private float _outlinerSize = 180f;
    private bool _loop;
    private bool _playing;
    internal int SelectedLayerIndex = -1;

    private readonly List<WaveformEditor> _layerEditors = [];

    public override bool ShowInspector => true;
    public override bool ShowInIsolation => true;
    public override bool RunInBackground => _playing;

    public new SoundDocument Document => (SoundDocument)base.Document;

    public CompositeSoundEditor(SoundDocument document) : base(document)
    {
        RebuildLayerEditors();

        Commands =
        [
            new Command { Name = "Toggle Playback", Handler = TogglePlayback, Key = InputCode.KeySpace },
            new Command { Name = "Frame", Handler = FrameWaveform, Key = InputCode.KeyF },
            new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab },
            new Command { Name = "Delete Layer", Handler = DeleteSelectedLayer, Key = InputCode.KeyDelete },
        ];

        FrameWaveform();
    }

    public override void Dispose()
    {
        _playing = false;
        Document.Stop();
        base.Dispose();
    }

    public override void Update()
    {
        if (_playing && !Document.IsPlaying)
        {
            if (_loop)
                Document.Play();
            else
                _playing = false;
        }

        if (_layerEditors.Count == 0) return;

        using (Gizmos.PushState(EditorLayer.Document))
        {
            Graphics.SetTransform(Document.Transform);

            // Draw composite bounds
            var duration = Document.Duration;
            if (duration > 0f)
            {
                var layerCount = Document.Layers.Count;
                var totalHeight = layerCount * (WaveformHeight + LayerSpacing) - LayerSpacing;
                var width = duration * WaveformEditor.WaveformScale;

                Gizmos.SetColor(EditorStyle.Workspace.BoundsColor.WithAlpha(0.3f));
                Gizmos.DrawRect(new Rect(0, -WaveformHeight * 0.5f, width, totalHeight),
                    EditorStyle.Workspace.DocumentBoundsLineWidth);
            }

            // Update handles on only one layer (the one being dragged, or the one under cursor)
            var activeHandleLayer = -1;
            for (var i = 0; i < _layerEditors.Count && i < Document.Layers.Count; i++)
            {
                if (_layerEditors[i].IsDragging)
                {
                    activeHandleLayer = i;
                    break;
                }
            }

            if (activeHandleLayer < 0)
            {
                // Find first layer with a hit
                for (var i = 0; i < _layerEditors.Count && i < Document.Layers.Count; i++)
                {
                    var layer = Document.Layers[i];
                    var top = LayerTopY(i);
                    var offsetX = layer.Offset * WaveformEditor.WaveformScale;
                    var handleDocPos = new Vector2(
                        Document.Position.X + offsetX,
                        Document.Position.Y + top);

                    var mouseWorld = Workspace.MouseWorldPosition;
                    var localX = mouseWorld.X - handleDocPos.X;
                    var localY = mouseWorld.Y - handleDocPos.Y;

                    if (_layerEditors[i].HitTest(localX, localY, WaveformHeight * 0.5f, Workspace.Zoom) != WaveformDragHandle.None)
                    {
                        activeHandleLayer = i;
                        break;
                    }
                }
            }

            if (activeHandleLayer >= 0)
            {
                var layer = Document.Layers[activeHandleLayer];
                var top = LayerTopY(activeHandleLayer);
                var offsetX = layer.Offset * WaveformEditor.WaveformScale;
                var handleDocPos = new Vector2(
                    Document.Position.X + offsetX,
                    Document.Position.Y + top);

                _layerEditors[activeHandleLayer].UpdateHandles(
                    handleDocPos, WaveformHeight * 0.5f,
                    () => Undo.Record(Document),
                    () => { Document.ApplyChanges(); RebuildLayerEditors(); });

                if (_layerEditors[activeHandleLayer].IsDragging)
                    SelectedLayerIndex = activeHandleLayer;
            }

            // Draw all layers
            for (var i = 0; i < _layerEditors.Count && i < Document.Layers.Count; i++)
            {
                var layer = Document.Layers[i];
                var editor = _layerEditors[i];
                var isSelected = i == SelectedLayerIndex;
                var top = LayerTopY(i);
                var offsetX = layer.Offset * WaveformEditor.WaveformScale;

                using (Graphics.PushState())
                {
                    Graphics.SetTransform(Matrix3x2.CreateTranslation(offsetX, top) * Document.Transform);

                    editor.Draw(
                        WaveformHeight * 0.5f,
                        isSelected ? ActiveAlpha : UnselectedAlpha,
                        isSelected: isSelected,
                        showBrackets: isSelected);

                    // Layer label
                    var labelText = layer.SoundRef.Name ?? "?";
                    Graphics.SetColor(EditorStyle.Palette.Content.WithAlpha(isSelected ? 1f : 0.6f));
                    using (Graphics.PushState())
                    {
                        Graphics.SetTransform(
                            Matrix3x2.CreateTranslation(0.02f, -WaveformHeight * 0.5f + 0.02f) *
                            Matrix3x2.CreateTranslation(offsetX, top) *
                            Document.Transform);
                        Graphics.DrawText(labelText, 0.05f);
                    }
                }
            }
        }
    }

    public override void UpdateUI()
    {
        using (UI.BeginRow())
        {
            using (UI.BeginFlex())
                OutlinerUI();

            UI.FlexSplitter(ElementId.OutlinerSplitter, ref _outlinerSize,
                EditorStyle.Inspector.Splitter, fixedPane: 1);

            using (UI.BeginFlex()) { }
        }
    }

    public override void UpdateOverlayUI()
    {
        using (FloatingToolbar.Begin())
        {
            if (FloatingToolbar.Button(ElementId.PlayButton, EditorAssets.Sprites.IconPlay, isSelected: _playing))
                TogglePlayback();

            if (FloatingToolbar.Button(ElementId.LoopButton, EditorAssets.Sprites.IconLoop, isSelected: _loop))
                _loop = !_loop;
        }
    }

    public override void OnUndoRedo()
    {
        RebuildLayerEditors();
        if (SelectedLayerIndex >= Document.Layers.Count)
            SelectedLayerIndex = Document.Layers.Count - 1;
    }

    private void RebuildLayerEditors()
    {
        _layerEditors.Clear();
        foreach (var layer in Document.Layers)
        {
            var editor = new WaveformEditor(layer);
            editor.BuildCache();
            _layerEditors.Add(editor);
        }
    }

    private void TogglePlayback()
    {
        if (_playing)
        {
            _playing = false;
            Document.Stop();
        }
        else
        {
            _playing = true;
            Document.Play();
        }
    }

    private void DeleteSelectedLayer()
    {
        if (SelectedLayerIndex < 0 || SelectedLayerIndex >= Document.Layers.Count)
            return;

        Undo.Record(Document);
        Document.RemoveLayer(SelectedLayerIndex);
        if (SelectedLayerIndex >= Document.Layers.Count)
            SelectedLayerIndex = Document.Layers.Count - 1;
        Document.ApplyChanges();
        RebuildLayerEditors();
        FrameWaveform();
    }

    private void FrameWaveform()
    {
        var duration = Document.Duration;
        if (duration <= 0f) duration = 1f;

        var layerCount = Math.Max(Document.Layers.Count, 1);
        var totalHeight = layerCount * (WaveformHeight + LayerSpacing);
        var width = duration * WaveformEditor.WaveformScale;

        var bounds = new Rect(
            Document.Position.X,
            Document.Position.Y - WaveformHeight * 0.5f,
            width,
            totalHeight);
        Workspace.FrameRect(bounds);
    }

    private float LayerTopY(int layerIndex)
    {
        return layerIndex * (WaveformHeight + LayerSpacing);
    }
}
