//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Reflection;
using NoZ.Platform;
using NoZ.Platform.WebGPU;

namespace NoZ.Editor;

public class EditorApplicationConfig
{
    public string Title { get; init; } = "NoZ Editor";
    public string? IconPath { get; init; }
    public string? EditorPath { get; init; }
    public string? ProjectPath { get; init; }
    public Action? RegisterAssetTypes { get; init; }
    public Action? RegisterDocumentTypes { get; init; }
    public Action? Update { get; init; }
    public Action? UpdateUI { get; init; }
    public Action<PropertySet>? LoadUserSettings { get; init; }
    public Action<PropertySet>? SaveUserSettings { get; init; }
    public Action<List<Command>>? RegisterCommands { get; init; }
    public Assembly? ResourceAssembly { get; init; }
    public IEditorStore? Store { get; init; }
    public bool IsTablet { get; init; }
}

internal class EditorApplicationInstance : IApplication
{
    public void Update() => EditorApplication.Update();
    public void LateUpdate() => EditorApplication.LateUpdate();

    public void LoadConfig(ApplicationConfig config)
    {
        var props = UserSettings.LoadPropertySet();
        if (props == null) return;

        UI.UserScale = props.GetFloat("workspace", "ui_scale", EditorApplication.DefaultUIScale);

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

    public void LoadAssets()
    {        
        EditorAssets.LoadAssets();
        EditorApplication.PostLoad();
    }

    public void UnloadAssets() => EditorAssets.UnloadAssets();
    public void ReloadAssets() => EditorAssets.ReloadAssets();
}

public static partial class EditorApplication
{
    private const float UIScaleMin = 0.5f;
    private const float UIScaleMax = 3f;
    private const float UIScaleStep = 0.1f;

    public enum AppPhase { Launching, Running }

    private static bool _clean;
    private static bool _isTablet;
    private static bool _projectInitialized;
    private static readonly Queue<Action> _mainThreadQueue = new();

    internal static EditorApplicationConfig AppConfig { get; private set; } = null!;

    public static IEditorStore Store { get; private set; } = null!;
    public static EditorConfig Config { get; private set; } = null!;
    public static string OutputPath { get; private set; } = null!;
    public static string EditorPath { get; private set; } = null!;
    public static string ProjectPath { get; private set; } = null!;
    public static AppPhase Phase { get; private set; } = AppPhase.Running;

    public static float DefaultUIScale => Application.IsTablet ? 1.6f : 1.2f;

    public static void LoadUserSettings(PropertySet props)
    {                
        AppConfig.LoadUserSettings?.Invoke(props);
    }

    public static void SaveUserSettings(PropertySet props)
    {
        var winSize = Application.WindowSize;
        var winPos = Application.WindowPosition;
        props.SetInt("window", "width", winSize.X);
        props.SetInt("window", "height", winSize.Y);
        props.SetInt("window", "x", winPos.X);
        props.SetInt("window", "y", winPos.Y);        

        props.SetFloat("workspace", "ui_scale", UI.UserScale);
        
        AppConfig.SaveUserSettings?.Invoke(props);
    }

    public static void Run(EditorApplicationConfig config, string[] args)
    {
#if false        
        AppConfig = config;
        var initProject = false;
        var exportOnly = false;
        var profilerMode = false;
        var clean = false;
        string? projectArg = null;
        string? editorPathArg = null;
        var isTablet = config.IsTablet || OperatingSystem.IsIOS();

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--profiler")
                profilerMode = true;
            else if (args[i] == "--project" && i + 1 < args.Length)
                projectArg = args[++i];
            else if (args[i] == "--editor-path" && i + 1 < args.Length)
                editorPathArg = args[++i];
            else if (args[i] == "--clean")
                clean = true;
            else if (args[i] == "--tablet")
                isTablet = true;
        }        

        _isTablet = isTablet;

        if (OperatingSystem.IsIOS())
        {
            var iosPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoZEditor");
            Directory.CreateDirectory(iosPath);
            //Init(iosPath, projectArg ?? iosPath, clean, config);
            Application.Init(new ApplicationConfig
            {
                Title = config.Title,
                Width = 1920,
                Height = 1080,
                HotReload = false,
                Platform = new SDLPlatform(),
                AudioBackend = new SDLAudioDriver(),
                Vtable = new EditorApplicationInstance(),
                AssetPath = Path.Combine(iosPath, "library"),
                ResourceAssembly = config.ResourceAssembly,
                IsTablet = isTablet,
                UI = new UIConfig()
                {
                    DefaultFont = EditorAssets.Names.Seguisb,
                    ScaleMode = UIScaleMode.ScaleWithScreenSize,
                    ReferenceResolution = new(1920, 1080),
                    ScreenMatchMode = ScreenMatchMode.MatchWidthOrHeight,
                    MatchWidthOrHeight = 0.5f
                },
                Graphics = new GraphicsConfig
                {
                    Driver = new WebGPUGraphicsDriver(),
                    PixelsPerUnit = Config?.PixelsPerUnit ?? 256,
                    Vsync = true,
                    HDR = true
                }
            });

            if (Application.IsTablet)
                Touch.SimulateMouse = true;

            Application.Run();
            return;
        }

        // Resolve editor path: use --editor-path if provided, otherwise walk up from app base directory
        string? editorPath = null;
        if (editorPathArg != null)
        {
            editorPath = Path.GetFullPath(editorPathArg);
        }
        else
        {
            // Look for directory with BOTH library/ AND NoZ.Editor.csproj
            editorPath = AppContext.BaseDirectory;
            while (editorPath != null)
            {
                if (Directory.Exists(Path.Combine(editorPath, "library")) &&
                    File.Exists(Path.Combine(editorPath, "NoZ.Editor.csproj")))
                    break;

                editorPath = Path.GetDirectoryName(editorPath);
            }
        }

        if (editorPath == null)
        {
            if (initProject)
            {
                // For --init mode, we don't need library/ to exist yet
                // Walk up from bin/Debug/net10.0 looking for NoZ.Editor.csproj
                editorPath = AppContext.BaseDirectory;
                for (var i = 0; i < 6; i++)
                {
                    var parent = Path.GetDirectoryName(editorPath);
                    if (parent == null) break;
                    editorPath = parent;
                    if (File.Exists(Path.Combine(editorPath, "NoZ.Editor.csproj")))
                        break;
                }

                Directory.CreateDirectory(Path.Combine(editorPath, "library"));
            }
            else
            {
                Log.Error("Could not find editor directory. Expected to find library/ folder and NoZ.Editor.csproj");
                return;
            }
        }

        // Desktop tablet simulation: use the project path as a sandbox and
        // skip the editor.cfg walk — the Launcher will pick a real store.
        // --project overrides the default temp sandbox location.
        if (isTablet && config.Store == null)
        {
            if (projectArg == null)
                projectPath = Path.Combine(Path.GetTempPath(), "NoZEditor");
            Directory.CreateDirectory(projectPath);
            Log.Info($"Tablet mode: using sandbox project path {projectPath}");
        }
        else if (config.Store == null)
        {
            // When a store is provided (e.g. GitStore), skip the directory walk —
            // the store already has the files at the project path.
            if (!File.Exists(Path.Combine(projectPath, "editor.cfg")))
            {
                Log.Info("Searching for project path...");
                projectPath = Path.GetDirectoryName(projectPath)!;
                while (projectPath != null)
                {
                    Log.Info($"Trying {projectPath}");
                    if (File.Exists(Path.Combine(projectPath, "editor.cfg")))
                        break;

                    projectPath = Path.GetDirectoryName(projectPath)!;
                }

                if (projectPath == null)
                {
                    Log.Error("Could not find project path (no 'editor.cfg' found)");
                    return;
                }
            }
        }

        Log.Info($"Editor Path: {editorPath}");
        Log.Info($"Project Path: {projectPath}");

        // Profiler mode: skip editor init, launch profiler viewer directly
        if (profilerMode)
        {
            ProfilerApplication.Run(editorPath);
            return;
        }

        Init(config);

        // Export-only mode: export assets and exit without launching GUI
        if (exportOnly)
        {
            DocumentManager.GenerateSpritesAsync().GetAwaiter().GetResult();

            Log.Info("Export complete.");
            DocumentManager.Shutdown();
            return;
        }

//#if DEBUG
        Profiler.Enabled = true;
//#endif

        var uiConfig = isTablet
            ? new UIConfig()
            {
                DefaultFont = EditorAssets.Names.Seguisb,
                ScaleMode = UIScaleMode.ScaleWithScreenSize,
                ReferenceResolution = new(1920, 1080),
                ScreenMatchMode = ScreenMatchMode.MatchWidthOrHeight,
                MatchWidthOrHeight = 0.5f,
            }
            : new UIConfig()
            {
                DefaultFont = EditorAssets.Names.Seguisb,
                ScaleMode = UIScaleMode.ConstantPixelSize,
            };

        Application.Init(new ApplicationConfig
        {
            Title = config.Title,
            Width = isTablet ? 1920 : 1600,
            Height = isTablet ? 1080 : 900,
            HotReload = false,
            IconPath = config.IconPath ?? "res/windows/nozed.png",
            Platform = new SDLPlatform(),
            AudioBackend = new SDLAudioDriver(),
            Vtable = new EditorApplicationInstance(),
            AssetPath = Path.Combine(EditorPath, "library"),
            IsTablet = isTablet,
            ResourceAssembly = config.ResourceAssembly,
            UI = uiConfig,
            Graphics = new GraphicsConfig
            {
                Driver = new WebGPUGraphicsDriver(),
                PixelsPerUnit = Config?.PixelsPerUnit ?? 256,
                Vsync = true,
                HDR = true
            }
        });

        if (Application.IsTablet)
            Touch.SimulateMouse = true;

        Application.Run();

        // On iOS, Run() returns immediately — CADisplayLink drives frames.
        // Shutdown will be called when the app terminates.
        if (OperatingSystem.IsIOS())
            return;

        Shutdown();
        Application.Shutdown();
#endif        
    }


    private static void Init(EditorApplicationConfig config)
    {
        EditorPath = config.EditorPath!;
        ProjectPath = config.ProjectPath!;
        _projectInitialized = false;

        // Only tablet/iOS mode shows the launcher — non-tablet desktop goes
        // straight to LocalStore with the resolved project path. When in
        // Launching phase, the LocalStore here is a benign sentinel so
        // downstream code (user.cfg load, PostLoad) has a valid Store; the
        // launcher swaps in a real store via BeginProject().
        var useLauncher = AppConfig.Store == null && (OperatingSystem.IsIOS() || _isTablet);
        //Store = AppConfig.Store ?? new LocalStore(ProjectPath);
        Phase = useLauncher ? AppPhase.Launching : AppPhase.Running;

        // Register asset/document types (static registrations, no files needed)
        Application.RegisterAssetTypes();
        config.RegisterAssetTypes?.Invoke();


        // If store is ready, initialize the project now.
        // When in Launching phase the launcher will drive BeginProject().
        if (Phase == AppPhase.Running && Store.IsReady)
            InitProject();
    }

    public static void BeginProject(IEditorStore store, string projectPath)
    {
        if (Phase != AppPhase.Launching)
        {
            Log.Warning("BeginProject called while not in Launching phase; ignoring.");
            return;
        }

        Store.Dispose();
        Store = store;
        ProjectPath = projectPath;
        //Store.Init(projectPath);
        Phase = AppPhase.Running;
        _projectInitialized = false;
    }

    public static void ReturnToLauncher()
    {
        if (Phase == AppPhase.Launching)
            return;

        if (_projectInitialized)
        {
            Log.Warning("ReturnToLauncher after project is fully initialized is not supported in this phase.");
            return;
        }

        Store.Dispose();
        var sentinel = new LocalStore(ProjectPath);
        Store = sentinel;
        Phase = AppPhase.Launching;
    }

    private static void InitProject()
    {
        Config = EditorConfig.Load("editor.cfg")!;
        if (Config == null)
        {
            Log.Warning("editor.cfg not found");
            return;
        }

        OutputPath = Config.OutputPath;
        Log.Info($"OutputPath: {OutputPath}");

        CollectionManager.Init(Config);
        Project.Init(ProjectPath, Config);
        PaletteManager.Init();
        Project.LoadAll();
        PaletteManager.DiscoverPalettes();
        AtlasManager.Init();
        Project.InitExports();
        AssetManifest.Generate();

        _projectInitialized = true;
    }

    internal static void PostLoad()
    {
        // UI infrastructure — always init so the store can draw its setup UI
        EditorStyle.Init();
        PopupMenu.Init();
        ConfirmDialog.Init();
        EditorCursor.Init();

        //Cursor.Enabled = !Application.IsTablet;

        if (Config == null)
            return;

        Project.PostLoad();
        AtlasManager.RebuildTextureArray();
        VfxSystem.Shader = EditorAssets.Shaders.Sprite;
        Workspace.Init();
        UserSettings.Load();

        Project.SaveAll();
        Project.QueueGenerations();
    }

    private static void Shutdown()
    {
        if (_projectInitialized)
        {
            UserSettings.Save();
            Project.SaveAll();

            Workspace.Shutdown();
            ConfirmDialog.Shutdown();
            PopupMenu.Shutdown();
            EditorStyle.Shutdown();
            CollectionManager.Shutdown();
            PaletteManager.Shutdown();
            Project.Shutdown();
        }

        Store.Dispose();
        Store = null!;
        Config = null!;
    }

    public static void RunOnMainThread(Action action)
    {
        lock (_mainThreadQueue)
            _mainThreadQueue.Enqueue(action);
    }

    internal static void Update()
    {
        lock (_mainThreadQueue)
            while (_mainThreadQueue.Count > 0)
                _mainThreadQueue.Dequeue().Invoke();

        // Launcher owns the screen until the user picks a project.
        if (Phase == AppPhase.Launching)
        {
            ProjectLauncher.UpdateUI();
            return;
        }

        // If store isn't ready, show its setup UI and wait
        if (!Store.IsReady)
        {
            Store.UpdateUI();
            return;
        }

        // First frame after store becomes ready — initialize the project
        if (!_projectInitialized)
        {
            InitProject();
            if (Config != null)
            {
                Project.UpdateExports();
                EditorAssets.ReloadAssets();

                // Project-specific PostLoad (UI infra already initialized)
                Project.PostLoad();
                AtlasManager.RebuildTextureArray();
                VfxSystem.Shader = EditorAssets.Shaders.Sprite;
                Workspace.Init();
                UserSettings.Load();
                Project.SaveAll();
                Project.QueueGenerations();
            }
        }

        if (Config == null)
            return;

        if (!Application.HasFocus && !(Workspace.ActiveEditor is { RunInBackground: true}))
            Thread.Sleep(1000 / 30);

        Project.UpdateExports();
        ConfirmDialog.Update();
        CommandPalette.Update();
        AssetPalette.Update();
        PopupMenu.Update();
        Workspace.Update();
        AppConfig.Update?.Invoke();

        Workspace.UpdateUI();
        PopupMenu.UpdateUI();
        CommandPalette.UpdateUI();
        AssetPalette.UpdateUI();
        ConfirmDialog.UpdateUI();
        AppConfig.UpdateUI?.Invoke();
    }

    internal static void LateUpdate()
    {
        Workspace.LateUpdate();
    }

    public static void IncreaseUIScale() =>
        UI.UserScale = Math.Clamp(UI.UserScale + UIScaleStep, UIScaleMin, UIScaleMax);

    public static void DecreaseUIScale() =>
        UI.UserScale = Math.Clamp(UI.UserScale - UIScaleStep, UIScaleMin, UIScaleMax);

    public static void ResetUIScale() =>
        UI.UserScale = DefaultUIScale;
}
