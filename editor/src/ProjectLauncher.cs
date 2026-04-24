//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static partial class ProjectLoader
{
    public enum Kind { Git, Networked }

    private static Kind _selectedKind = Kind.Networked;
    private static string _hostInput = "";
    private static string _portInput = RemoteProtocol.DefaultPort.ToString();
    private static string _errorMessage = "";

    public static void Init()
    {        
    }

    public static void UpdateUI()
    {
        using (UI.BeginColumn(new ContainerStyle
        {
            Width = Size.Percent(),
            Height = Size.Percent(),
            Align = Align.Center,
            Spacing = 16,
            Padding = new EdgeInsets(32, 32, 32, 32),
        }))
        {
            using (UI.BeginFlex()) { }

            UI.Text("Open a Project", EditorStyle.Text.Primary);
            UI.Text("Pick where the project files come from.", EditorStyle.Text.Disabled);

            DrawKindSelector();

            using (UI.BeginColumn(new ContainerStyle { Spacing = 8, Width = new Size(360) }))
            {
                switch (_selectedKind)
                {
                    case Kind.Git:
                        DrawGitForm();
                        break;
                    case Kind.Networked:
                        DrawNetworkedForm();
                        break;
                }
            }

            if (!string.IsNullOrEmpty(_errorMessage))
                UI.Text(_errorMessage, EditorStyle.Text.Disabled);

            if (UI.Button(WidgetIds.OpenButton, "Open", EditorStyle.Button.Primary))
                TryOpen();

            using (UI.BeginFlex()) { }
        }
    }

    private static void DrawKindSelector()
    {
        using (UI.BeginRow(new ContainerStyle { Spacing = 8 }))
        {
            if (UI.Button(WidgetIds.KindGitButton, "Git", KindButtonStyle(Kind.Git)))
                _selectedKind = Kind.Git;

            if (UI.Button(WidgetIds.KindNetworkedButton, "Networked", KindButtonStyle(Kind.Networked)))
                _selectedKind = Kind.Networked;
        }
    }

    private static ButtonStyle KindButtonStyle(Kind kind) =>
        _selectedKind == kind ? EditorStyle.Button.Primary : EditorStyle.Button.Secondary;

    private static void DrawGitForm()
    {
        UI.Text("Sign in to GitHub on the next screen to pick a repo.", EditorStyle.Text.Disabled);
    }

    private static void DrawNetworkedForm()
    {
        UI.Text("Host", EditorStyle.Text.Disabled);
        _hostInput = UI.TextInput(WidgetIds.HostInput, _hostInput, EditorStyle.TextInput, "192.168.1.42");

        UI.Text("Port", EditorStyle.Text.Disabled);
        _portInput = UI.TextInput(WidgetIds.PortInput, _portInput, EditorStyle.TextInput, RemoteProtocol.DefaultPort.ToString());
    }

    private static void TryOpen()
    {
        _errorMessage = "";

        switch (_selectedKind)
        {
            case Kind.Git:
                OpenGit();
                break;
            case Kind.Networked:
                OpenNetworked();
                break;
        }
    }

    private static void OpenGit()
    {
        //var cachePath = GitStore.GetDefaultCachePath();
        //EditorApplication.BeginProject(new GitStore(GitStore.DefaultClientId), cachePath);
    }

    private static void OpenNetworked()
    {
        var host = _hostInput.Trim();
        if (string.IsNullOrEmpty(host))
        {
            _errorMessage = "Enter a host (e.g. 192.168.1.42).";
            return;
        }

        if (!int.TryParse(_portInput, out var port) || port <= 0 || port > 65535)
            port = RemoteProtocol.DefaultPort;

        //var cachePath = RemoteStore.GetCachePath(host, port);
        //EditorApplication.BeginProject(new RemoteStore(host, port), cachePath);
    }

    private static partial class WidgetIds
    {
        public static partial WidgetId KindGitButton { get; }
        public static partial WidgetId KindNetworkedButton { get; }
        public static partial WidgetId HostInput { get; }
        public static partial WidgetId PortInput { get; }
        public static partial WidgetId OpenButton { get; }
    }
}
