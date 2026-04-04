//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal partial class SoundEditor : DocumentEditor
{
    private static partial class ElementId
    {
        public static partial WidgetId PlayButton { get; }
        public static partial WidgetId LoopButton { get; }
    }

    private const float WaveformHeight = 1f;

    private bool _loop;
    private bool _playing;
    private readonly WaveformEditor _waveform;

    public override bool ShowInspector => true;
    public override bool ShowInIsolation => true;
    public override bool RunInBackground => _playing;

    public new SoundDocument Document => (SoundDocument)base.Document;

    private Matrix3x2 BoundsTransform =>
        Matrix3x2.CreateTranslation(Document.Bounds.Position) * Document.Transform;

    private Vector2 BoundsOrigin => Document.Position + Document.Bounds.Position;

    public SoundEditor(SoundDocument document) : base(document)
    {
        _waveform = new WaveformEditor(document);
        _waveform.BuildCache();

        Commands =
        [
            new Command ("Toggle Playback", TogglePlayback,     [InputCode.KeySpace]),
            new Command ("Frame",           FrameWaveform,      [InputCode.KeyF]),
            new Command ("Exit Edit Mode",  Workspace.EndEdit,  [InputCode.KeyTab])
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

        using (Gizmos.PushState(EditorLayer.Document))
        {
            Graphics.SetTransform(BoundsTransform);

            _waveform.UpdateHandles(
                BoundsOrigin, WaveformHeight * 0.5f,
                () => Undo.Record(Document),
                () => { Document.ApplyChanges(); _waveform.BuildCache(); });

            _waveform.Draw(
                WaveformHeight * 0.5f,
                0.85f,
                isSelected: true,
                showBrackets: false);
        }

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(BoundsTransform);
            _waveform.DrawOverlay(
                WaveformHeight * 0.5f,
                showBrackets: true,
                playbackPosition: _playing && Document.IsPlaying ? Document.PlaybackPosition : -1f,
                isPlaying: _playing);
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
        _waveform.BuildCache();
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

    private void FrameWaveform()
    {
        var width = Document.Duration * WaveformEditor.WaveformScale;
        if (width <= 0f) width = 1f;
        var origin = BoundsOrigin;
        var bounds = new Rect(
            origin.X,
            origin.Y - WaveformHeight * 0.5f,
            width,
            WaveformHeight);
        Workspace.FrameRect(bounds);
    }
}
