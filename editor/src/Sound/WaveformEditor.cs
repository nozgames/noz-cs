//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal interface IWaveformSource
{
    float[]? GetMonoSamples();
    int SampleRate { get; }
    float Duration { get; }

    float TrimStart { get; set; }
    float TrimEnd { get; set; }
    float FadeIn { get; set; }
    float FadeOut { get; set; }
    float Offset { get; set; }
    bool OffsetEnabled { get; }

    float NormalizeScale { get; }
}

internal enum WaveformDragHandle { None, TrimStart, TrimEnd, FadeIn, FadeOut, Offset }

internal class WaveformEditor
{
    public const float WaveformScale = 2f;

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

    // Waveform cache
    private float[]? _waveformMin;
    private float[]? _waveformMax;
    private int _waveformLength;

    // Drag state
    private WaveformDragHandle _activeDrag;
    private bool _undoRecorded;
    private float _savedFadeIn;
    private float _savedFadeOut;
    private float _dragStartOffset;
    private float _dragStartMouseX;

    public IWaveformSource Source { get; }
    public WaveformDragHandle ActiveDrag => _activeDrag;

    public WaveformEditor(IWaveformSource source)
    {
        Source = source;
    }

    // --- Cache ---

    public void BuildCache(int maxBuckets = 2048)
    {
        var samples = Source.GetMonoSamples();
        if (samples == null || samples.Length == 0)
        {
            _waveformMin = null;
            _waveformMax = null;
            _waveformLength = 0;
            return;
        }

        var normalizeScale = Source.NormalizeScale;
        var bucketCount = Math.Min(samples.Length, maxBuckets);
        var samplesPerBucket = (float)samples.Length / bucketCount;

        _waveformMin = new float[bucketCount];
        _waveformMax = new float[bucketCount];
        _waveformLength = bucketCount;

        for (var b = 0; b < bucketCount; b++)
        {
            var start = (int)(b * samplesPerBucket);
            var end = (int)((b + 1) * samplesPerBucket);
            end = Math.Min(end, samples.Length);

            var min = float.MaxValue;
            var max = float.MinValue;

            for (var s = start; s < end; s++)
            {
                var value = samples[s] * normalizeScale;
                if (value < min) min = value;
                if (value > max) max = value;
            }

            _waveformMin[b] = min;
            _waveformMax[b] = max;
        }
    }

    // --- Draw ---
    // Caller must set graphics transform before calling. All coords are local.

    public void Draw(float halfHeight, float alpha, bool isSelected, bool showBrackets,
        float playbackPosition = -1f, bool isPlaying = false)
    {
        if (_waveformMin == null || _waveformMax == null || _waveformLength == 0)
            return;

        var duration = Source.Duration;
        if (duration <= 0f) return;

        var waveformWidth = duration * WaveformScale;
        var trimStart = Source.TrimStart;
        var trimEnd = Source.TrimEnd > 0f ? Source.TrimEnd : 1f;

        // Background
        Gizmos.SetColor(EditorStyle.Palette.Canvas.WithAlpha(BackgroundAlpha));
        Graphics.Draw(0, -halfHeight, waveformWidth, halfHeight * 2f);

        // Center line
        Gizmos.SetColor(EditorStyle.Palette.SecondaryText.WithAlpha(BackgroundAlpha));
        Gizmos.DrawLine(new Vector2(0, 0), new Vector2(waveformWidth, 0), CenterLineWidth);

        // Waveform bars
        var bucketCount = _waveformLength;
        var barWidth = waveformWidth / bucketCount;

        for (var i = 0; i < bucketCount; i++)
        {
            var t = (float)i / bucketCount;
            var outsideTrim = t < trimStart || t > trimEnd;
            Gizmos.SetColor(WaveformColor.WithAlpha(outsideTrim ? TrimmedAlpha : alpha));

            var min = _waveformMin[i] * halfHeight;
            var max = _waveformMax[i] * halfHeight;
            var x = i * barWidth;

            // Mirror: bar spans from -absMax to +absMax
            var absMax = MathF.Max(MathF.Abs(min), MathF.Abs(max));
            if (absMax < barWidth * MinBarHeightRatio * 0.5f)
                absMax = barWidth * MinBarHeightRatio * 0.5f;
            Graphics.Draw(x, -absMax, barWidth, absMax * 2f);
        }

    }

    // Draw overlay elements (fade, trim brackets, playback head) — call at a higher layer
    public void DrawOverlay(float halfHeight, bool showBrackets,
        float playbackPosition = -1f, bool isPlaying = false)
    {
        var duration = Source.Duration;
        if (duration <= 0f) return;

        var waveformWidth = duration * WaveformScale;
        var trimStart = Source.TrimStart;
        var trimEnd = Source.TrimEnd > 0f ? Source.TrimEnd : 1f;

        var hoverHandle = _activeDrag != WaveformDragHandle.None ? _activeDrag : HitTest(halfHeight, Workspace.Zoom);

        var trimStartX = trimStart * waveformWidth;
        var trimEndX = trimEnd * waveformWidth;
        var fadeInX = (trimStart + Source.FadeIn) * waveformWidth;
        var fadeOutX = (trimEnd - Source.FadeOut) * waveformWidth;

        if (Source.FadeIn > 0f)
        {
            Gizmos.SetColor(Color.Black.WithAlpha(hoverHandle == WaveformDragHandle.FadeIn ? FadeOverlayActiveAlpha : FadeOverlayAlpha));
            Gizmos.DrawTriangle(
                new Vector2(trimStartX, -halfHeight),
                new Vector2(fadeInX, -halfHeight),
                new Vector2(trimStartX, halfHeight));
        }

        if (Source.FadeOut > 0f)
        {
            Gizmos.SetColor(Color.Black.WithAlpha(hoverHandle == WaveformDragHandle.FadeOut ? FadeOverlayActiveAlpha : FadeOverlayAlpha));
            Gizmos.DrawTriangle(
                new Vector2(trimEndX, -halfHeight),
                new Vector2(fadeOutX, -halfHeight),
                new Vector2(trimEndX, halfHeight));
        }

        if (showBrackets)
        {
            var tabLength = MinTrimGap * waveformWidth;

            // [ bracket
            Gizmos.SetColor(hoverHandle == WaveformDragHandle.TrimStart ? EditorStyle.Palette.PrimaryHover : EditorStyle.Palette.Primary);
            Gizmos.DrawLine(new Vector2(trimStartX, -halfHeight), new Vector2(trimStartX, halfHeight), LineWidth, extendEnds: true);
            Gizmos.DrawLine(new Vector2(trimStartX, -halfHeight), new Vector2(trimStartX + tabLength, -halfHeight), LineWidth, extendEnds: true);
            Gizmos.DrawLine(new Vector2(trimStartX, halfHeight), new Vector2(trimStartX + tabLength, halfHeight), LineWidth, extendEnds: true);

            // ] bracket
            Gizmos.SetColor(hoverHandle == WaveformDragHandle.TrimEnd ? EditorStyle.Palette.PrimaryHover : EditorStyle.Palette.Primary);
            Gizmos.DrawLine(new Vector2(trimEndX, -halfHeight), new Vector2(trimEndX, halfHeight), LineWidth, extendEnds: true);
            Gizmos.DrawLine(new Vector2(trimEndX, -halfHeight), new Vector2(trimEndX - tabLength, -halfHeight), LineWidth, extendEnds: true);
            Gizmos.DrawLine(new Vector2(trimEndX, halfHeight), new Vector2(trimEndX - tabLength, halfHeight), LineWidth, extendEnds: true);
        }
        else
        {
            // Non-selected: thin black lines at trim positions
            if (trimStart > 0f)
            {
                Gizmos.SetColor(Color.Black.WithAlpha(0.5f));
                Gizmos.DrawLine(new Vector2(trimStartX, -halfHeight), new Vector2(trimStartX, halfHeight), LineWidth * 0.5f);
            }
            if (trimEnd < 1f)
            {
                Gizmos.SetColor(Color.Black.WithAlpha(0.5f));
                Gizmos.DrawLine(new Vector2(trimEndX, -halfHeight), new Vector2(trimEndX, halfHeight), LineWidth * 0.5f);
            }
        }

        if (isPlaying && playbackPosition >= 0f)
        {
            var norm = trimStart + playbackPosition * (trimEnd - trimStart);
            var headX = norm * waveformWidth;
            Gizmos.SetColor(EditorStyle.Palette.Content);
            Gizmos.DrawLine(
                new Vector2(headX, -halfHeight),
                new Vector2(headX, halfHeight),
                LineWidth,
                extendEnds: true);
        }
    }

    // --- Hit testing ---
    // localX/localY are relative to waveform origin (0,0)

    public WaveformDragHandle HitTest(float halfHeight, float zoom)
    {
        var mouseWorld = Workspace.MouseWorldPosition;
        // Caller is responsible for providing correct local coords via the transform
        // We need the raw local coords — but since we don't know the transform, we use a method
        // that takes pre-computed local coords instead.
        return WaveformDragHandle.None;
    }

    public WaveformDragHandle HitTest(float localX, float localY, float halfHeight, float zoom)
    {
        if (localY < -halfHeight * HitTestYPadding || localY > halfHeight * HitTestYPadding)
            return WaveformDragHandle.None;

        var duration = Source.Duration;
        if (duration <= 0f) return WaveformDragHandle.None;

        var hitDist = HandleHitDistance / zoom;
        var waveformWidth = duration * WaveformScale;

        var trimStart = Source.TrimStart;
        var trimEnd = Source.TrimEnd > 0f ? Source.TrimEnd : 1f;
        var trimStartX = trimStart * waveformWidth;
        var trimEndX = trimEnd * waveformWidth;

        // Top 1/3 = fade zone
        var inFadeZone = localY < -halfHeight + halfHeight * 2f * FadeZoneRatio;

        if (inFadeZone)
        {
            var fadeInX = (trimStart + Source.FadeIn) * waveformWidth;
            var fadeOutX = (trimEnd - Source.FadeOut) * waveformWidth;

            var tY = (halfHeight - localY) / (halfHeight * 2f);

            var fadeInMaxX = trimStartX + (fadeInX - trimStartX) * tY;
            if (localX >= trimStartX - hitDist && localX <= MathF.Max(fadeInMaxX, trimStartX + hitDist))
                return WaveformDragHandle.FadeIn;

            var fadeOutMinX = trimEndX - (trimEndX - fadeOutX) * tY;
            if (localX <= trimEndX + hitDist && localX >= MathF.Min(fadeOutMinX, trimEndX - hitDist))
                return WaveformDragHandle.FadeOut;
        }

        // Trim handles
        if (MathF.Abs(localX - trimStartX) < hitDist)
            return WaveformDragHandle.TrimStart;

        if (MathF.Abs(localX - trimEndX) < hitDist)
            return WaveformDragHandle.TrimEnd;

        // Middle area = offset drag (only if enabled)
        if (Source.OffsetEnabled && localX > trimStartX + hitDist && localX < trimEndX - hitDist)
            return WaveformDragHandle.Offset;

        return WaveformDragHandle.None;
    }

    // --- Handle interaction ---

    public void UpdateHandles(Vector2 docPosition, float halfHeight, Action recordUndo, Action applyChanges)
    {
        var duration = Source.Duration;
        if (duration <= 0f) return;

        var waveformWidth = duration * WaveformScale;
        var mouseWorld = Workspace.MouseWorldPosition;

        // Compute local mouse position (docPosition already includes offset if applicable)
        var localX = mouseWorld.X - docPosition.X;
        var localY = mouseWorld.Y - docPosition.Y;

        if (_activeDrag != WaveformDragHandle.None)
        {
            if (_activeDrag == WaveformDragHandle.Offset)
                EditorCursor.SetSystem(SystemCursor.Move);
            else
                EditorCursor.SetSystem(SystemCursor.ResizeEW);

            if (Input.IsButtonDown(InputCode.MouseLeft))
            {
                if (!_undoRecorded)
                {
                    recordUndo();
                    _undoRecorded = true;
                }

                var norm = Math.Clamp(localX / waveformWidth, 0f, 1f);

                switch (_activeDrag)
                {
                    case WaveformDragHandle.TrimStart:
                    {
                        var trimEnd = Source.TrimEnd > 0f ? Source.TrimEnd : 1f;
                        Source.TrimStart = Math.Clamp(norm, 0f, trimEnd - MinTrimGap);

                        var trimRange = trimEnd - Source.TrimStart;
                        Source.FadeIn = Math.Min(_savedFadeIn, trimRange - Source.FadeOut);
                        Source.FadeIn = MathF.Max(Source.FadeIn, 0f);
                        break;
                    }
                    case WaveformDragHandle.TrimEnd:
                    {
                        Source.TrimEnd = Math.Clamp(norm, Source.TrimStart + MinTrimGap, 1f);

                        var trimRange = Source.TrimEnd - Source.TrimStart;
                        Source.FadeOut = Math.Min(_savedFadeOut, trimRange - Source.FadeIn);
                        Source.FadeOut = MathF.Max(Source.FadeOut, 0f);
                        break;
                    }
                    case WaveformDragHandle.FadeIn:
                    {
                        var projected = ProjectFade(localX, localY, halfHeight, Source.TrimStart * waveformWidth, waveformWidth);
                        var fade = projected - Source.TrimStart;
                        var trimEnd = Source.TrimEnd > 0f ? Source.TrimEnd : 1f;
                        var maxFade = (trimEnd - Source.TrimStart) - Source.FadeOut;
                        Source.FadeIn = Math.Clamp(fade, 0f, maxFade);
                        break;
                    }
                    case WaveformDragHandle.FadeOut:
                    {
                        var trimEnd = Source.TrimEnd > 0f ? Source.TrimEnd : 1f;
                        var projected = ProjectFade(localX, localY, halfHeight, trimEnd * waveformWidth, waveformWidth);
                        var fade = trimEnd - projected;
                        var maxFade = (trimEnd - Source.TrimStart) - Source.FadeIn;
                        Source.FadeOut = Math.Clamp(fade, 0f, maxFade);
                        break;
                    }
                    case WaveformDragHandle.Offset:
                    {
                        var mouseDeltaX = mouseWorld.X - _dragStartMouseX;
                        Source.Offset = MathF.Max(0f, _dragStartOffset + mouseDeltaX / WaveformScale);
                        break;
                    }
                }

                applyChanges();
            }
            else
            {
                _activeDrag = WaveformDragHandle.None;
                _undoRecorded = false;
            }
        }
        else
        {
            var hover = HitTest(localX, localY, halfHeight, Workspace.Zoom);

            if (hover != WaveformDragHandle.None)
            {
                if (hover == WaveformDragHandle.Offset)
                    EditorCursor.SetSystem(SystemCursor.Move);
                else
                    EditorCursor.SetSystem(SystemCursor.ResizeEW);

                if (Input.WasButtonPressed(InputCode.MouseLeft))
                {
                    _activeDrag = hover;
                    recordUndo();
                    _undoRecorded = true;

                    if (hover is WaveformDragHandle.TrimStart or WaveformDragHandle.TrimEnd)
                    {
                        _savedFadeIn = Source.FadeIn;
                        _savedFadeOut = Source.FadeOut;
                    }

                    if (hover == WaveformDragHandle.Offset)
                    {
                        _dragStartOffset = Source.Offset;
                        _dragStartMouseX = mouseWorld.X;
                    }

                    if (hover is WaveformDragHandle.FadeIn or WaveformDragHandle.FadeOut)
                        applyChanges();
                }
            }
        }
    }

    public bool IsDragging => _activeDrag != WaveformDragHandle.None;

    private static float ProjectFade(float localX, float localY, float halfHeight, float trimEdgeX, float waveformWidth)
    {
        var dy = halfHeight - localY;
        if (dy <= 0f) return trimEdgeX / waveformWidth;

        var projectedX = trimEdgeX + (localX - trimEdgeX) * (halfHeight * 2f) / dy;
        return projectedX / waveformWidth;
    }
}
