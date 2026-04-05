//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal static partial class FloatingPanel
{
    private static partial class ElementId
    {
        public static partial WidgetId Header { get; }
    }

    private static WidgetId _ownerId;
    private static Vector2 _position;
    private static Vector2 _dragStart;
    private static Vector2 _dragPositionStart;
    private static bool _dragging;

    public static bool IsOpen(WidgetId id) => _ownerId == id;

    public static void Open(WidgetId id, Vector2 position)
    {
        _ownerId = id;
        _position = position;
        _dragging = false;
    }

    public static void Close()
    {
        _ownerId = WidgetId.None;
        _dragging = false;
    }

    public struct Auto : IDisposable
    {
        readonly void IDisposable.Dispose() => End();
    }

    public static Auto Begin(WidgetId id, ContainerStyle style)
    {
        UI.BeginColumn();
        ElementTree.BeginMargin(EdgeInsets.TopLeft(_position.Y, _position.X));

        style.Width = Size.Fit;
        style.Height = Size.Fit;
        UI.BeginColumn(style);
        DrawHeader(style.Width);
        return new Auto();
    }

    public static void End()
    {
        UI.EndColumn();
        ElementTree.EndMargin();
        UI.EndColumn();
    }

    private static void DrawHeader(Size width)
    {
        ref var trackState = ref ElementTree.BeginWidget<TrackState>(ElementId.Header);
        ElementTree.BeginTrack(ref trackState, ElementId.Header, 1, 1);

        using (UI.BeginContainer(new ContainerStyle
        {
            Width = width,
            Height = 8,
            Background = EditorStyle.Palette.Control,
            BorderRadius = 2,
        }))
        {
        }

        if (UI.IsDown())
        {
            if (UI.WasPressed())
            {
                _dragging = true;
                _dragStart = Input.MousePosition;
                _dragPositionStart = _position;
            }

            if (_dragging)
                _position = _dragPositionStart + (Input.MousePosition - _dragStart);
        }
        else
        {
            _dragging = false;
        }

        ElementTree.EndTrack();
        ElementTree.EndWidget();
    }
}
