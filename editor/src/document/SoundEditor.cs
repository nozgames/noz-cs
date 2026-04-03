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

    private enum DragHandle { None, TrimStart, TrimEnd, FadeIn, FadeOut }

    private const float WaveformHeight = 1f;
    private const float WaveformScale = 2f;
    private const float HandleHitDistance = 0.08f;
    private const float MinTrimGap = 0.01f;
    private const float FadeZoneRatio = 0.333f;
    private const float LineWidth = 0.015f;
    private const float CenterLineWidth = 0.01f;
    private const float TrimmedAlpha = 0.2f;
    private const float ActiveAlpha = 0.85f;
    private const float BackgroundAlpha = 0.3f;
    private const float FadeOverlayAlpha = 0.3f;
    private const float FadeOverlayActiveAlpha = 0.4f;
    private const float HitTestYPadding = 1.2f;
    private const float BarWidthRatio = 0.9f;
    private const float MinBarHeightRatio = 0.5f;

    private static readonly Color WaveformColor = new(0.4f, 0.7f, 1.0f);

    private bool _loop;
    private bool _playing;
    private DragHandle _activeDrag;
    private bool _undoRecorded;

    private float _savedFadeIn;
    private float _savedFadeOut;

    public override bool ShowInspector => true;
    public override bool ShowInIsolation => true;
    public override bool RunInBackground => _playing;

    public new SoundDocument Document => (SoundDocument)base.Document;

    public SoundEditor(SoundDocument document) : base(document)
    {
        Commands =
        [
            new Command { Name = "Toggle Playback", Handler = TogglePlayback, Key = InputCode.KeySpace },
            new Command { Name = "Frame", Handler = FrameWaveform, Key = InputCode.KeyF },
            new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab },
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

        UpdateHandles();
        DrawWaveform();
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
        var width = Document.Duration * WaveformScale;
        if (width <= 0f) width = 1f;
        var bounds = new Rect(
            Document.Position.X,
            Document.Position.Y - WaveformHeight * 0.5f,
            width,
            WaveformHeight);
        Workspace.FrameRect(bounds);
    }

    private float MouseToNormalized()
    {
        var duration = Document.Duration;
        if (duration <= 0f) return 0f;
        var localX = Workspace.MouseWorldPosition.X - Document.Position.X;
        return localX / (duration * WaveformScale);
    }

    // Project a line from trimEdge bottom through cursor to the top edge of the waveform.
    // Returns the normalized X position where that line hits the top, which is the fade size.
    private float ProjectFadeFromCursor(float trimEdgeNorm)
    {
        var duration = Document.Duration;
        if (duration <= 0f) return 0f;

        var mouseWorld = Workspace.MouseWorldPosition;
        var localX = mouseWorld.X - Document.Position.X;
        var localY = mouseWorld.Y - Document.Position.Y;
        var halfHeight = WaveformHeight * 0.5f;
        var waveformWidth = duration * WaveformScale;
        var trimEdgeX = trimEdgeNorm * waveformWidth;

        // If cursor is at or below the trim bottom, fade is zero
        var dy = halfHeight - localY;
        if (dy <= 0f) return 0f;

        // Intersect line from (trimEdgeX, halfHeight) through (localX, localY) with y = -halfHeight
        var projectedX = trimEdgeX + (localX - trimEdgeX) * WaveformHeight / dy;
        return projectedX / waveformWidth;
    }

    private float TrimStartNorm => Document.TrimStart;
    private float TrimEndNorm => Document.TrimEnd > 0f ? Document.TrimEnd : 1f;
    private float TrimRange => TrimEndNorm - TrimStartNorm;

    private void SnapFadeToCursor(DragHandle handle)
    {
        if (handle == DragHandle.FadeIn)
        {
            var projectedNorm = ProjectFadeFromCursor(TrimStartNorm);
            var fade = projectedNorm - TrimStartNorm;
            var maxFade = TrimRange - Document.FadeOut;
            Document.FadeIn = Math.Clamp(fade, 0f, maxFade);
        }
        else if (handle == DragHandle.FadeOut)
        {
            var projectedNorm = ProjectFadeFromCursor(TrimEndNorm);
            var fade = TrimEndNorm - projectedNorm;
            var maxFade = TrimRange - Document.FadeIn;
            Document.FadeOut = Math.Clamp(fade, 0f, maxFade);
        }
    }

    private DragHandle HitTestHandles()
    {
        var duration = Document.Duration;
        if (duration <= 0f) return DragHandle.None;

        var mouseWorld = Workspace.MouseWorldPosition;
        var localX = mouseWorld.X - Document.Position.X;
        var localY = mouseWorld.Y - Document.Position.Y;
        var halfHeight = WaveformHeight * 0.5f;

        if (localY < -halfHeight * HitTestYPadding || localY > halfHeight * HitTestYPadding)
            return DragHandle.None;

        var hitDist = HandleHitDistance / Workspace.Zoom;
        var waveformWidth = duration * WaveformScale;

        var trimStartX = TrimStartNorm * waveformWidth;
        var trimEndX = TrimEndNorm * waveformWidth;

        // Top 1/3 = fade zone — clicking anywhere in the fade region starts a fade drag
        var inFadeZone = localY < -halfHeight + WaveformHeight * FadeZoneRatio;

        if (inFadeZone)
        {
            // Fade-in triangle: (trimStartX, -halfHeight), (fadeInX, -halfHeight), (trimStartX, halfHeight)
            // Hypotenuse runs from (trimStartX, halfHeight) to (fadeInX, -halfHeight)
            // At mouse Y, the max X inside the triangle is interpolated along the hypotenuse
            var fadeInX = (TrimStartNorm + Document.FadeIn) * waveformWidth;
            var fadeOutX = (TrimEndNorm - Document.FadeOut) * waveformWidth;

            // t=0 at bottom (halfHeight), t=1 at top (-halfHeight)
            var tY = (halfHeight - localY) / WaveformHeight;

            // Fade in: inside triangle or within hitDist of trim edge (to start from zero)
            var fadeInMaxX = trimStartX + (fadeInX - trimStartX) * tY;
            if (localX >= trimStartX - hitDist && localX <= MathF.Max(fadeInMaxX, trimStartX + hitDist))
                return DragHandle.FadeIn;

            // Fade out: mirror — hypotenuse from (trimEndX, halfHeight) to (fadeOutX, -halfHeight)
            var fadeOutMinX = trimEndX - (trimEndX - fadeOutX) * tY;
            if (localX <= trimEndX + hitDist && localX >= MathF.Min(fadeOutMinX, trimEndX - hitDist))
                return DragHandle.FadeOut;
        }

        // Bottom 2/3 = trim handles (on the bracket lines)
        if (MathF.Abs(localX - trimStartX) < hitDist)
            return DragHandle.TrimStart;

        if (MathF.Abs(localX - trimEndX) < hitDist)
            return DragHandle.TrimEnd;

        return DragHandle.None;
    }

    private void UpdateHandles()
    {
        var duration = Document.Duration;
        if (duration <= 0f) return;

        if (_activeDrag != DragHandle.None)
        {
            Cursor.Set(SystemCursor.ResizeEW);

            if (Input.IsButtonDown(InputCode.MouseLeft))
            {
                var norm = Math.Clamp(MouseToNormalized(), 0f, 1f);

                if (!_undoRecorded)
                {
                    Undo.Record(Document);
                    _undoRecorded = true;
                }

                switch (_activeDrag)
                {
                    case DragHandle.TrimStart:
                    {
                        var maxStart = TrimEndNorm - MinTrimGap;
                        Document.TrimStart = Math.Clamp(norm, 0f, maxStart);

                        // Fade moves with trim, but clamp if space shrinks
                        var availableRange = TrimRange;
                        Document.FadeIn = Math.Min(_savedFadeIn, availableRange - Document.FadeOut);
                        Document.FadeIn = MathF.Max(Document.FadeIn, 0f);
                        break;
                    }
                    case DragHandle.TrimEnd:
                    {
                        var minEnd = Document.TrimStart + MinTrimGap;
                        Document.TrimEnd = Math.Clamp(norm, minEnd, 1f);

                        var availableRange = TrimRange;
                        Document.FadeOut = Math.Min(_savedFadeOut, availableRange - Document.FadeIn);
                        Document.FadeOut = MathF.Max(Document.FadeOut, 0f);
                        break;
                    }
                    case DragHandle.FadeIn:
                    case DragHandle.FadeOut:
                        SnapFadeToCursor(_activeDrag);
                        break;
                }

                Document.ApplyChanges();
            }
            else
            {
                _activeDrag = DragHandle.None;
                _undoRecorded = false;
            }
        }
        else
        {
            var hover = HitTestHandles();

            if (hover != DragHandle.None)
            {
                Cursor.Set(SystemCursor.ResizeEW);

                if (Input.WasButtonPressed(InputCode.MouseLeft))
                {
                    _activeDrag = hover;
                    Undo.Record(Document);
                    _undoRecorded = true;

                    // Save original fade values for trim drags
                    if (hover is DragHandle.TrimStart or DragHandle.TrimEnd)
                    {
                        _savedFadeIn = Document.FadeIn;
                        _savedFadeOut = Document.FadeOut;
                    }

                    // Snap fade to cursor on click
                    if (hover is DragHandle.FadeIn or DragHandle.FadeOut)
                    {
                        SnapFadeToCursor(hover);
                        Document.ApplyChanges();
                    }
                }
            }
        }
    }

    private void DrawWaveform()
    {
        if (Document.WaveformMin == null || Document.WaveformMax == null || Document.WaveformLength == 0)
            return;

        var duration = Document.Duration;
        if (duration <= 0f) return;

        var waveformWidth = duration * WaveformScale;
        var halfHeight = WaveformHeight * 0.5f;
        var trimStart = TrimStartNorm;
        var trimEnd = TrimEndNorm;

        using (Gizmos.PushState(EditorLayer.Document))
        {
            Graphics.SetTransform(Document.Transform);

            Gizmos.SetColor(EditorStyle.Palette.Canvas.WithAlpha(BackgroundAlpha));
            Graphics.Draw(0, -halfHeight, waveformWidth, WaveformHeight);

            Gizmos.SetColor(EditorStyle.Palette.SecondaryText.WithAlpha(BackgroundAlpha));
            Gizmos.DrawLine(new Vector2(0, 0), new Vector2(waveformWidth, 0), CenterLineWidth);

            var bucketCount = Document.WaveformLength;
            var barWidth = waveformWidth / bucketCount;

            for (var i = 0; i < bucketCount; i++)
            {
                var t = (float)i / bucketCount;
                var outsideTrim = t < trimStart || t > trimEnd;
                Gizmos.SetColor(WaveformColor.WithAlpha(outsideTrim ? TrimmedAlpha : ActiveAlpha));

                var min = Document.WaveformMin[i] * halfHeight;
                var max = Document.WaveformMax[i] * halfHeight;
                var x = i * barWidth;
                var barH = max - min;
                if (barH < barWidth * MinBarHeightRatio) barH = barWidth * MinBarHeightRatio;

                Graphics.Draw(x, min, barWidth * BarWidthRatio, barH);
            }

            Gizmos.SetColor(EditorStyle.Workspace.BoundsColor);
            Gizmos.DrawRect(new Rect(0, -halfHeight, waveformWidth, WaveformHeight),
                EditorStyle.Workspace.DocumentBoundsLineWidth);

            var tabLength = MinTrimGap * waveformWidth;
            var hoverHandle = _activeDrag != DragHandle.None ? _activeDrag : HitTestHandles();

            var trimStartX = trimStart * waveformWidth;
            var trimEndX = trimEnd * waveformWidth;
            var fadeInX = (trimStart + Document.FadeIn) * waveformWidth;
            var fadeOutX = (trimEnd - Document.FadeOut) * waveformWidth;

            if (Document.FadeIn > 0f)
            {
                Gizmos.SetColor(Color.Black.WithAlpha(hoverHandle == DragHandle.FadeIn ? FadeOverlayActiveAlpha : FadeOverlayAlpha));
                Gizmos.DrawTriangle(
                    new Vector2(trimStartX, -halfHeight),
                    new Vector2(fadeInX, -halfHeight),
                    new Vector2(trimStartX, halfHeight));
            }

            if (Document.FadeOut > 0f)
            {
                Gizmos.SetColor(Color.Black.WithAlpha(hoverHandle == DragHandle.FadeOut ? FadeOverlayActiveAlpha : FadeOverlayAlpha));
                Gizmos.DrawTriangle(
                    new Vector2(trimEndX, -halfHeight),
                    new Vector2(fadeOutX, -halfHeight),
                    new Vector2(trimEndX, halfHeight));
            }

            // [ bracket
            {
                Gizmos.SetColor(hoverHandle == DragHandle.TrimStart ? EditorStyle.Palette.PrimaryHover : EditorStyle.Palette.Primary);
                Gizmos.DrawLine(new Vector2(trimStartX, -halfHeight), new Vector2(trimStartX, halfHeight), LineWidth, extendEnds: true);
                Gizmos.DrawLine(new Vector2(trimStartX, -halfHeight), new Vector2(trimStartX + tabLength, -halfHeight), LineWidth, extendEnds: true);
                Gizmos.DrawLine(new Vector2(trimStartX, halfHeight), new Vector2(trimStartX + tabLength, halfHeight), LineWidth, extendEnds: true);
            }

            // ] bracket
            {
                Gizmos.SetColor(hoverHandle == DragHandle.TrimEnd ? EditorStyle.Palette.PrimaryHover : EditorStyle.Palette.Primary);
                Gizmos.DrawLine(new Vector2(trimEndX, -halfHeight), new Vector2(trimEndX, halfHeight), LineWidth, extendEnds: true);
                Gizmos.DrawLine(new Vector2(trimEndX, -halfHeight), new Vector2(trimEndX - tabLength, -halfHeight), LineWidth, extendEnds: true);
                Gizmos.DrawLine(new Vector2(trimEndX, halfHeight), new Vector2(trimEndX - tabLength, halfHeight), LineWidth, extendEnds: true);
            }

            if (_playing && Document.IsPlaying)
            {
                var pos = Document.PlaybackPosition;
                var norm = trimStart + pos * (trimEnd - trimStart);
                var headX = norm * waveformWidth;
                Gizmos.SetColor(EditorStyle.Palette.Content);
                Gizmos.DrawLine(
                    new Vector2(headX, -halfHeight),
                    new Vector2(headX, halfHeight),
                    LineWidth,
                    extendEnds: true);
            }
        }
    }
}
