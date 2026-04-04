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
    private const float LayerSpacing = 0f;
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

    private Matrix3x2 BoundsTransform =>
        Matrix3x2.CreateTranslation(Document.Bounds.Position) * Document.Transform;

    private Vector2 BoundsOrigin => Document.Position + Document.Bounds.Position;

    public CompositeSoundEditor(SoundDocument document) : base(document)
    {
        RebuildLayerEditors();

        Commands =
        [
            new Command("Toggle Playback",  TogglePlayback,         [InputCode.KeySpace]),
            new Command("Frame",            FrameWaveform,          [InputCode.KeyF]),
            new Command("Exit Edit Mode",   Workspace.EndEdit,      [InputCode.KeyTab]),
            new Command("Delete Layer",     DeleteSelectedLayer,    [InputCode.KeyX, InputCode.KeyDelete]),
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
            Graphics.SetTransform(BoundsTransform);

            // Draw composite bounds (visual extent, not just trimmed export)
            var maxVisualEnd = 0f;
            foreach (var layer in Document.Layers)
            {
                var src = layer.SoundRef.Value;
                if (src == null) continue;
                var layerEnd = layer.Offset + src.Duration;
                if (layerEnd > maxVisualEnd) maxVisualEnd = layerEnd;
            }

            if (maxVisualEnd > 0f)
            {
                var layerCount = Document.Layers.Count;
                var totalHeight = layerCount * (WaveformHeight + LayerSpacing) - LayerSpacing;
                var width = maxVisualEnd * WaveformEditor.WaveformScale;

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

            var boundsOrigin = BoundsOrigin;

            if (activeHandleLayer < 0)
            {
                // Find first layer with a hit
                for (var i = 0; i < _layerEditors.Count && i < Document.Layers.Count; i++)
                {
                    var layer = Document.Layers[i];
                    var top = LayerTopY(i);
                    var offsetX = layer.Offset * WaveformEditor.WaveformScale;
                    var handleDocPos = new Vector2(
                        boundsOrigin.X + offsetX,
                        boundsOrigin.Y + top);

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
                    boundsOrigin.X + offsetX,
                    boundsOrigin.Y + top);

                _layerEditors[activeHandleLayer].UpdateHandles(
                    handleDocPos, WaveformHeight * 0.5f,
                    () => Undo.Record(Document),
                    () => { Document.ApplyChanges(); RebuildLayerCaches(); });

                if (_layerEditors[activeHandleLayer].IsDragging)
                    SelectedLayerIndex = activeHandleLayer;
            }

            // Click anywhere in a layer's bounds to select it
            if (Input.WasButtonPressed(InputCode.MouseLeft) && activeHandleLayer < 0)
            {
                var mouseWorld = Workspace.MouseWorldPosition;
                for (var i = 0; i < Document.Layers.Count; i++)
                {
                    var layer = Document.Layers[i];
                    var src = layer.SoundRef.Value;
                    if (src == null) continue;

                    var top = LayerTopY(i);
                    var offsetX = layer.Offset * WaveformEditor.WaveformScale;
                    var halfH = WaveformHeight * 0.5f;
                    var waveformWidth = src.Duration * WaveformEditor.WaveformScale;

                    var localX = mouseWorld.X - boundsOrigin.X - offsetX;
                    var localY = mouseWorld.Y - boundsOrigin.Y - top;

                    if (localX >= 0 && localX <= waveformWidth && localY >= -halfH && localY <= halfH)
                    {
                        SelectedLayerIndex = i;
                        break;
                    }
                }
            }

            // Draw all layer waveforms
            for (var i = 0; i < _layerEditors.Count && i < Document.Layers.Count; i++)
            {
                var layer = Document.Layers[i];
                var editor = _layerEditors[i];
                var isSelected = i == SelectedLayerIndex;
                var top = LayerTopY(i);
                var offsetX = layer.Offset * WaveformEditor.WaveformScale;

                using (Graphics.PushState())
                {
                    Graphics.SetTransform(Matrix3x2.CreateTranslation(offsetX, top) * BoundsTransform);
                    editor.Draw(
                        WaveformHeight * 0.5f,
                        isSelected ? ActiveAlpha : UnselectedAlpha,
                        isSelected: isSelected,
                        showBrackets: false);
                }
            }
        }

        // Overlays at higher layer (trim brackets, fade, labels, playback head)
        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(BoundsTransform);

            for (var i = 0; i < _layerEditors.Count && i < Document.Layers.Count; i++)
            {
                var layer = Document.Layers[i];
                var editor = _layerEditors[i];
                var isSelected = i == SelectedLayerIndex;
                var top = LayerTopY(i);
                var offsetX = layer.Offset * WaveformEditor.WaveformScale;

                using (Graphics.PushState())
                {
                    Graphics.SetTransform(Matrix3x2.CreateTranslation(offsetX, top) * BoundsTransform);
                    editor.DrawOverlay(
                        WaveformHeight * 0.5f,
                        showBrackets: isSelected);
                }

                // Layer label
                var labelText = layer.SoundRef.Name ?? "?";
                Graphics.SetColor(EditorStyle.Palette.Content.WithAlpha(isSelected ? 1f : 0.6f));
                using (Graphics.PushState())
                {
                    Graphics.SetTransform(
                        Matrix3x2.CreateTranslation(0.02f, -WaveformHeight * 0.5f + 0.02f) *
                        Matrix3x2.CreateTranslation(offsetX, top) *
                        BoundsTransform);
                    Graphics.DrawText(labelText, 0.025f);
                }
            }

            // Playback head spanning all layers
            if (_playing && Document.IsPlaying)
            {
                var compositeDuration = Document.Duration;
                if (compositeDuration > 0f)
                {
                    var pos = Document.PlaybackPosition;
                    var headX = pos * compositeDuration * WaveformEditor.WaveformScale;
                    var layerCount = Document.Layers.Count;
                    var totalHeight = layerCount * (WaveformHeight + LayerSpacing);

                    Gizmos.SetColor(EditorStyle.Palette.Content);
                    Gizmos.DrawLine(
                        new Vector2(headX, -WaveformHeight * 0.5f),
                        new Vector2(headX, totalHeight - WaveformHeight * 0.5f),
                        0.015f,
                        extendEnds: true);
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

    private void RebuildLayerCaches()
    {
        for (var i = 0; i < _layerEditors.Count; i++)
            _layerEditors[i].BuildCache();
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

        var origin = BoundsOrigin;
        var bounds = new Rect(
            origin.X,
            origin.Y - WaveformHeight * 0.5f,
            width,
            totalHeight);
        Workspace.FrameRect(bounds);
    }

    private float LayerTopY(int layerIndex)
    {
        return layerIndex * (WaveformHeight + LayerSpacing);
    }
}
