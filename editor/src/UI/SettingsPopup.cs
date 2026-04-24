using NoZ;

namespace NoZ.Editor;

public static partial class SettingsPopup
{
    private static partial class WidgetIds
    {
        public static partial WidgetId Popup { get; }
        public static partial WidgetId Background { get; }
        public static partial WidgetId Close { get; }
        public static partial WidgetId IncrementUIScale { get; }
        public static partial WidgetId DecrementUIScale { get; }
        public static partial WidgetId ResetUIScale { get; }
        public static partial WidgetId ShowFPSToggle { get; }
    }

    private static InputScope _inputScope;

    public static bool IsOpen { get; private set; }

    public static void Open()
    {
        if (IsOpen) return;

        IsOpen = true;
        _inputScope = Input.PushScope();
    }

    public static void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        Input.PopScope(_inputScope);
    }

    public static void Update()
    {
        if (!IsOpen) return;

        using var cursor = UI.BeginCursor(new SpriteCursor(EditorAssets.Sprites.CursorArrow));
        using var popup = UI.BeginPopup(WidgetIds.Popup,new PopupStyle{ AutoClose = false });
        using var root = UI.BeginContainer(EditorStyle.Popup.Root with { Padding = 16, Height = Size.Fit, Width = 300 });
        using var column = UI.BeginColumn(new ContainerStyle { Spacing = 4 });

        using (UI.BeginRow())
        {
            UI.Text("Settings");    
            UI.Flex();
            if (UI.Button(WidgetIds.Close, EditorAssets.Sprites.IconClose, EditorStyle.Button.IconOnly))
                Close();
        }        

        using var general = Inspector.BeginSection("General");

        using (Inspector.BeginProperty("UI Scale"))
        {
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                using (UI.BeginRow())
                {
                    UI.Text(Strings.Number((int)(UI.UserScale * 100)), EditorStyle.Text.Disabled);    
                    UI.Text("%", EditorStyle.Text.Disabled);
                }

                if (UI.Button(WidgetIds.DecrementUIScale, EditorAssets.Sprites.IconRemove, EditorStyle.Button.IconOnly))
                    EditorApplication.DecreaseUIScale();
                if (UI.Button(WidgetIds.IncrementUIScale, EditorAssets.Sprites.IconAdd, EditorStyle.Button.IconOnly))
                    EditorApplication.IncreaseUIScale();
                if (UI.Button(WidgetIds.ResetUIScale, EditorAssets.Sprites.IconRefresh, EditorStyle.Button.IconOnly))
                    EditorApplication.ResetUIScale();
            }
        }

        using (Inspector.BeginProperty("Show FPS"))
        {
            if (UI.Toggle(WidgetIds.ShowFPSToggle, Workspace.ShowFps, EditorStyle.Inspector.Toggle))
                Workspace.ToggleShowFps();
        }

        if (Input.WasButtonPressed(InputCode.KeyEscape, _inputScope))
            Close();
    }
}
