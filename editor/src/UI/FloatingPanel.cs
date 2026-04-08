//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal static partial class FloatingPanel
{
    private static partial class ElementId
    {
        public static partial WidgetId Backdrop { get; }
        public static partial WidgetId Panel { get; }
    }

    private static WidgetId _ownerId;
    private static Vector2 _position;
    private static bool _backdropPressed;

    public static bool IsOpen(WidgetId id) => _ownerId == id;
    public static bool WasBackdropPressed => _backdropPressed;

    public static void Open(WidgetId id, Vector2 position)
    {
        _ownerId = id;
        _position = position;
    }

    public static void Close()
    {
        _ownerId = WidgetId.None;
    }

    public struct Auto : IDisposable
    {
        readonly void IDisposable.Dispose() => End();
    }

    public static Auto Begin(WidgetId id, ContainerStyle style)
    {
        UI.BeginContainer(ElementId.Backdrop);
        _backdropPressed = UI.WasPressed();

        // Clamp position to screen bounds using previous frame's panel size
        var prevRect = UI.GetElementWorldRect(ElementId.Panel);
        if (prevRect.Width > 0 && prevRect.Height > 0)
        {
            var screen = UI.ScreenSize;
            _position.X = Math.Clamp(_position.X, 0, Math.Max(0, screen.X - prevRect.Width));
            _position.Y = Math.Clamp(_position.Y, 0, Math.Max(0, screen.Y - prevRect.Height));
        }

        ElementTree.BeginMargin(EdgeInsets.TopLeft(_position.Y, _position.X));

        // Non-interactive widget to track panel size across frames
        ElementTree.BeginWidget(ElementId.Panel, interactive: false);

        style.AlignX = Align.Min;
        style.AlignY = Align.Min;
        style.Width = Size.Fit;
        style.Height = Size.Fit;
        UI.BeginColumn(style);
        return new Auto();
    }

    public static void End()
    {
        UI.EndColumn();
        ElementTree.EndWidget();
        ElementTree.EndMargin();
        UI.EndContainer();
    }
}
