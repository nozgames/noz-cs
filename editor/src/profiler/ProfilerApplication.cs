//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ.Platform;
using NoZ.Platform.WebGPU;

namespace NoZ.Editor;

internal class ProfilerApplicationInstance : IApplication
{
    public void Update() => ProfilerApplication.Update();
    public void LateUpdate() { }

    public void LoadConfig(ApplicationConfig config)
    {
        var props = PropertySet.LoadFile(ProfilerApplication.ConfigPath);
        if (props == null) return;

        var w = props.GetInt("window", "width", 0);
        var h = props.GetInt("window", "height", 0);
        if (w > 100 && h > 100)
        {
            config.Width = w;
            config.Height = h;
        }

        var x = props.GetInt("window", "x", int.MinValue);
        var y = props.GetInt("window", "y", int.MinValue);
        if (x != int.MinValue && y != int.MinValue)
        {
            config.X = x;
            config.Y = y;
        }
    }

    public void SaveConfig()
    {
        var props = new PropertySet();
        var size = Application.WindowSize;
        var pos = Application.WindowPosition;
        props.SetInt("window", "width", size.X);
        props.SetInt("window", "height", size.Y);
        props.SetInt("window", "x", pos.X);
        props.SetInt("window", "y", pos.Y);
        props.Save(ProfilerApplication.ConfigPath);
    }

    public void LoadAssets()
    {
        EditorAssets.LoadAssets();
    }

    public void UnloadAssets() => EditorAssets.UnloadAssets();
    public void ReloadAssets() => EditorAssets.ReloadAssets();
}

public static partial class ProfilerApplication
{
    private static ProfilerServer _server = null!;
    private static bool _paused;
    private static int _selectedFrame = -1;
    private static string _editorPath = null!;

    // Snapshot of buffer state when paused so the graph freezes
    private static int _pausedCount;
    private static int _pausedNewestIndex;

    internal static ProfilerServer Server => _server;
    internal static bool Paused => _paused;
    internal static int PausedCount => _pausedCount;
    internal static int PausedNewestIndex => _pausedNewestIndex;
    internal static string ConfigPath => Path.Combine(_editorPath, "profiler.cfg");
    internal static int SelectedFrame
    {
        get => _selectedFrame;
        set => _selectedFrame = value;
    }

    public static void Run(string editorPath)
    {
        _editorPath = editorPath;
        _server = new ProfilerServer();

        Application.RegisterAssetTypes();

        Application.Init(new ApplicationConfig
        {
            Title = "NoZ Profiler",
            Width = 1200,
            Height = 700,
            Platform = new SDLPlatform(),
            AudioBackend = new SDLAudioDriver(),
            Vtable = new ProfilerApplicationInstance(),
            AssetPath = Path.Combine(editorPath, "library"),
            UI = new UIConfig()
            {
                DefaultFont = EditorAssets.Names.Seguisb,
                ScaleMode = UIScaleMode.ConstantPixelSize,
            },
            Graphics = new GraphicsConfig
            {
                Driver = new WebGPUGraphicsDriver(),
                Vsync = true
            }
        });

        _server.Start();
        Log.Info("Profiler viewer started");

        UI.UserScale = 1.2f;

        Application.Run();

        _server.Stop();
        Application.Shutdown();
    }

    internal static void Update()
    {
        if (!_paused)
        {
            _server.Update();

            if (_server.Buffer.Count > 0)
            {
                _selectedFrame = _server.Buffer.NewestIndex;
                _pausedCount = _server.Buffer.Count;
                _pausedNewestIndex = _server.Buffer.NewestIndex;
            }
        }
        else
        {
            _server.Drain();
        }

        ProfilerUI.Draw();
    }

    internal static void TogglePause()
    {
        _paused = !_paused;
        if (_paused)
        {
            _pausedCount = _server.Buffer.Count;
            _pausedNewestIndex = _server.Buffer.NewestIndex;
        }
    }
}
