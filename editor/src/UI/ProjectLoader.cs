//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static partial class ProjectLoader
{
    private enum State { None, Connecting, Syncing, Connected }
    private enum Kind { Git, Networked }

    private static Kind _selectedKind = Kind.Networked;
    private static string _hostInput = "";
    private static string _portInput = RemoteProtocol.DefaultPort.ToString();
    private static string _errorMessage = "";
    private static string _rootPath = null!;
    private static IProjectSync? _sync;
    private static State _state = State.None;

    public static void Init(string rootPath)
    {
        _rootPath = rootPath;

        var configPath = Path.Combine(rootPath, "project.cfg");
        var config = PropertySet.LoadFile(configPath);
        if (config != null)
        {
            var host = config.GetString("remote", "host", "");
            var port = config.GetInt("remote", "port", 0);
                _hostInput = host;
            _portInput = port.ToString();

            var kind = config.GetString("project", "kind", "");
            if (kind == "git")
                _selectedKind = Kind.Git;
            else if (kind == "networked")
                _selectedKind = Kind.Networked;
        }
    }

    public static void SaveConfig()
    {
        var configPath = Path.Combine(_rootPath, "project.cfg");
        var config = new PropertySet();
        config.SetString("project", "kind", _selectedKind == Kind.Git ? "git" : "networked");
        config.SetString("remote", "host", _hostInput);
        if (int.TryParse(_portInput, out var port))
            config.SetInt("remote", "port", port);

        config.Save(configPath);
    }

    public static void UpdateUI()
    {
        using var column = UI.BeginColumn(new ContainerStyle
        {
            Align = Align.Center,
            Padding = 16,
            Spacing = 16,
            Height = 200
        });

        if (_state == State.Connecting)
        {
            UI.Text("Connecting...", EditorStyle.Text.Primary);
            return;
        }

        if (_state == State.Syncing)
        {
            UI.Text("Syncing...", EditorStyle.Text.Primary);
            return;
        }

        if (_state == State.Connected)
        {
            if (!EditorApplication.LoadProject(_sync!))
            {
                _sync!.Dispose();
                _state = State.None;
                _errorMessage = "Failed to load project.";
            }
        }
            
        using (UI.BeginRow(new ContainerStyle {Spacing = 16}))
        {
            UpdateKind();

            using (UI.BeginColumn(new ContainerStyle { Spacing = 8, Width = new Size(200) }))
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

                if (!string.IsNullOrEmpty(_errorMessage))
                    UI.Text(_errorMessage, EditorStyle.Text.Disabled);

                if (UI.Button(WidgetIds.OpenButton, "Open", EditorStyle.Button.Primary))
                    TryOpen();
            }
        }
    }

    private static void UpdateKind()
    {
        using (UI.BeginColumn(new ContainerStyle { Spacing = 8 }))
        {
            if (UI.Button(WidgetIds.KindGitButton, "Git", KindButtonStyle(Kind.Git)))
                _selectedKind = Kind.Git;

            if (UI.Button(WidgetIds.KindNetworkedButton, "Networked", KindButtonStyle(Kind.Networked)))
                _selectedKind = Kind.Networked;
        }
    }

    private static ButtonStyle KindButtonStyle(Kind kind) =>
        (_selectedKind == kind
            ? EditorStyle.Button.Primary 
            : EditorStyle.Button.Secondary) with { Width = new Size(80) };

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

        SaveConfig();

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

        _state = State.Connecting;
        _sync = null;

        RemoteSync.ConnectAsync(host, port, _rootPath).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                _state = State.Syncing;

                t.Result.SyncAsync().ContinueWith(t2 =>
                {
                    if (t2.IsCompletedSuccessfully)
                    {
                        _state = State.Connected;             
                        _sync = t.Result;                        
                    }
                    else
                    {
                        _state = State.None;
                        t2.Dispose();
                        _errorMessage = "Failed to sync with host.";
                    }
                });
                return;
            }
            else
            {
                t.Dispose();
                _state = State.None;
                _errorMessage = "Failed to connect to host.";
            }
        });

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
