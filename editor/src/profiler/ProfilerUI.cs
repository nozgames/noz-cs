//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;

namespace NoZ.Editor;

public static partial class ProfilerUI
{
    private static partial class WidgetIds
    {
        public static partial WidgetId Pause { get; }
        public static partial WidgetId FrameBar { get; }
        public static partial WidgetId DetailScroll { get; }
    }

    private const float FrameGraphHeight = 150.0f;
    private const float BarWidth = 3.0f;
    private const float BarSpacing = 1.0f;
    private const float MarkerRowHeight = 22.0f;

    private static readonly Color FrameGood = Color.FromRgb(0x4EC94E);
    private static readonly Color FrameWarn = Color.FromRgb(0xE8C84E);
    private static readonly Color FrameBad = Color.FromRgb(0xE84E4E);
    private static readonly Color SelectionColor = Color.FromRgb(0x4E8CE8);

    private static Color GetFrameColor(float deltaTime)
    {
        var ms = deltaTime * 1000f;
        if (ms < 16.67f) return FrameGood;
        if (ms < 33.33f) return FrameWarn;
        return FrameBad;
    }

    public static void Draw()
    {
        using (UI.BeginColumn(new ContainerStyle
        {
            Background = EditorStyle.Palette.Control,
            Size = new Size2(Size.Percent(), Size.Percent())
        }))
        {
            DrawToolbar();
            UI.Separator(EditorStyle.Palette.Separator);
            DrawFrameGraph();
            UI.Separator(EditorStyle.Palette.Separator);
            DrawDetailPanel();
        }
    }

    private static void DrawToolbar()
    {
        using (UI.BeginRow(new ContainerStyle
        {
            Height = EditorStyle.Control.Height,
            Background = EditorStyle.Palette.Panel,
            Padding = EdgeInsets.Symmetric(EditorStyle.Control.Spacing, EditorStyle.Control.Spacing * 2),
            Spacing = EditorStyle.Control.Spacing * 2
        }))
        {
            var pauseText = ProfilerApplication.Paused ? "Resume" : "Pause";
            if (UI.Button(WidgetIds.Pause, pauseText, EditorStyle.Button.Secondary))
                ProfilerApplication.TogglePause();

            UI.Flex();

            var server = ProfilerApplication.Server;
            var statusStyle = server.Connected ? EditorStyle.Text.Primary : EditorStyle.Text.Secondary;
            UI.Text(server.Connected ? "Connected" : "Waiting...", statusStyle);

            UI.Spacer(16);
            UI.Text($"Frames: {server.Buffer.Count}", EditorStyle.Text.Secondary);

            var selected = GetSelectedFrame();
            if (selected != null)
            {
                UI.Spacer(16);
                var ms = selected.DeltaTime * 1000f;
                var fps = selected.DeltaTime > 0 ? 1f / selected.DeltaTime : 0;
                UI.Text($"Frame {selected.FrameNumber}  |  {ms:F2}ms  |  {fps:F0} FPS", EditorStyle.Text.Primary);
            }
        }
    }

    private static void DrawFrameGraph()
    {
        using (UI.BeginRow(new ContainerStyle
        {
            Height = FrameGraphHeight,
            Background = EditorStyle.Palette.Panel,
            Padding = EdgeInsets.All(EditorStyle.Control.Spacing * 2)
        }))
        {
            var buffer = ProfilerApplication.Server.Buffer;
            var paused = ProfilerApplication.Paused;
            var frameCount = paused ? ProfilerApplication.PausedCount : buffer.Count;
            var newestIndex = paused ? ProfilerApplication.PausedNewestIndex : buffer.NewestIndex;

            if (frameCount == 0)
            {
                UI.Flex();
                using (UI.BeginCenter())
                    UI.Text("No frame data", EditorStyle.Text.Secondary);
                UI.Flex();
                return;
            }

            var graphHeight = FrameGraphHeight - EditorStyle.Control.Spacing * 4;
            var maxMs = 33.33f;

            for (var i = 0; i < frameCount; i++)
            {
                var idx = (newestIndex - i + FrameRingBuffer.Capacity) % FrameRingBuffer.Capacity;
                var frame = buffer.Get(idx);
                if (frame != null)
                {
                    var ms = frame.DeltaTime * 1000f;
                    if (ms > maxMs) maxMs = ms;
                }
            }

            using (UI.BeginRow(new ContainerStyle
            {
                AlignY = Align.Max,
                Height = Size.Percent(),
            }))
            {
                for (var i = frameCount - 1; i >= 0; i--)
                {
                    var frameIndex = (newestIndex - i + FrameRingBuffer.Capacity) % FrameRingBuffer.Capacity;
                    var frame = buffer.Get(frameIndex);
                    if (frame == null) continue;

                    var ms = frame.DeltaTime * 1000f;
                    var barHeight = MathF.Max(2f, (ms / maxMs) * graphHeight);
                    var isSelected = frameIndex == ProfilerApplication.SelectedFrame;
                    var color = isSelected ? SelectionColor : GetFrameColor(frame.DeltaTime);

                    var barId = WidgetIds.FrameBar + i;

                    using (UI.BeginColumn(barId, new ContainerStyle
                    {
                        Width = BarWidth + BarSpacing,
                        Height = Size.Percent(),
                        Padding = EdgeInsets.Right(BarSpacing),
                    }))
                    {
                        UI.Flex();
                        UI.Container(new ContainerStyle
                        {
                            Height = barHeight,
                            Background = color,
                        });
                        if (isSelected)
                        {
                            UI.Container(new ContainerStyle
                            {
                                Height = 2,
                                Background = EditorStyle.Palette.Content,
                            });
                        }
                    }

                    if (UI.WasPressed(barId))
                    {
                        ProfilerApplication.SelectedFrame = frameIndex;
                        if (!ProfilerApplication.Paused)
                            ProfilerApplication.TogglePause();
                    }
                }
            }
        }
    }

    private static void DrawDetailPanel()
    {
        using (UI.BeginFlex())
        using (UI.BeginColumn(new ContainerStyle
        {
            Background = EditorStyle.Palette.Panel,
            Padding = EdgeInsets.All(EditorStyle.Control.Spacing * 2),
            Spacing = EditorStyle.Control.Spacing,
            Size = new Size2(Size.Percent(), Size.Percent())
        }))
        {
            var frame = GetSelectedFrame();
            if (frame == null)
            {
                UI.Flex();
                using (UI.BeginCenter())
                    UI.Text("Select a frame to view details", EditorStyle.Text.Secondary);
                UI.Flex();
                return;
            }

            using (UI.BeginRow(new ContainerStyle { Height = EditorStyle.Control.Height, Spacing = 16 }))
            {
                var ms = frame.DeltaTime * 1000f;
                var fps = frame.DeltaTime > 0 ? 1f / frame.DeltaTime : 0;
                UI.Text($"Frame {frame.FrameNumber}", EditorStyle.Text.Primary);
                UI.Text($"{ms:F2} ms", EditorStyle.Text.Primary with { Color = GetFrameColor(frame.DeltaTime) });
                UI.Text($"{fps:F0} FPS", EditorStyle.Text.Secondary);
            }

            UI.Spacer(2);
            UI.Separator(EditorStyle.Palette.Separator);
            UI.Spacer(2);

            using (UI.BeginFlex())
            using (UI.BeginScrollable(WidgetIds.DetailScroll))
            using (UI.BeginColumn(new ContainerStyle { Spacing = 1 }))
            {
                if (frame.MarkerCount > 0)
                {
                    UI.Text("Markers", EditorStyle.Text.Secondary);
                    UI.Spacer(2);

                    var frameTicks = (long)(frame.DeltaTime * Stopwatch.Frequency);
                    if (frameTicks <= 0) frameTicks = 1;

                    for (var i = 0; i < frame.MarkerCount; i++)
                    {
                        ref var marker = ref frame.Markers[i];
                        var markerMs = (float)marker.ElapsedTicks / Stopwatch.Frequency * 1000f;
                        var pct = (float)marker.ElapsedTicks / frameTicks * 100f;
                        var indent = marker.Depth * 16f;

                        using (UI.BeginRow(new ContainerStyle
                        {
                            Height = MarkerRowHeight,
                            Width = Size.Percent(1),
                            AlignY = Align.Center,
                            Background = i % 2 == 0 ? Color.Transparent : EditorStyle.Palette.Separator.WithAlpha(0.3f),
                        }))
                        {
                            UI.Spacer(indent);
                            if (marker.CallCount > 1)
                                UI.Text($"{marker.Name} ({marker.CallCount}x)", EditorStyle.Text.Primary);
                            else
                                UI.Text(marker.Name, EditorStyle.Text.Primary);
                            UI.Flex();
                            using (UI.BeginContainer(new ContainerStyle { Width = 60, AlignX = Align.Max }))
                            {
                                if (marker.AllocBytes > 0)
                                    UI.Text($"{marker.AllocBytes} B", EditorStyle.Text.Secondary with { Color = Color.FromRgb(0xE8A33A) });
                            }
                            using (UI.BeginContainer(new ContainerStyle { Width = 70, AlignX = Align.Max }))
                                UI.Text($"{markerMs:F3} ms", EditorStyle.Text.Secondary);
                            using (UI.BeginContainer(new ContainerStyle { Width = 45, AlignX = Align.Max }))
                                UI.Text($"{pct:F1}%", EditorStyle.Text.Secondary);
                        }
                    }
                }

                if (frame.CounterCount > 0)
                {
                    UI.Spacer(4);
                    UI.Separator(EditorStyle.Palette.Separator);
                    UI.Spacer(4);
                    UI.Text("Counters", EditorStyle.Text.Secondary);
                    UI.Spacer(2);

                    for (var i = 0; i < frame.CounterCount; i++)
                    {
                        ref var counter = ref frame.Counters[i];

                        using (UI.BeginRow(new ContainerStyle
                        {
                            Height = MarkerRowHeight,
                            Spacing = EditorStyle.Control.Spacing * 2,
                            Background = i % 2 == 0 ? Color.Transparent : EditorStyle.Palette.Separator.WithAlpha(0.3f),
                        }))
                        {
                            UI.Text(counter.Name, EditorStyle.Text.Primary);
                            UI.Flex();
                            UI.Text($"{counter.Value:F1}", EditorStyle.Text.Secondary);
                        }
                    }
                }
            }
        }
    }

    private static FrameData? GetSelectedFrame()
    {
        return ProfilerApplication.Server.Buffer.Get(ProfilerApplication.SelectedFrame);
    }
}
