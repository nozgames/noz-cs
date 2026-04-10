//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NoZ.Platform;
using NoZ.Platform.WebGPU;

namespace NoZ.Editor;

public class EditorApplicationConfig
{
    public string Title { get; init; } = "NoZ Editor";
    public string? IconPath { get; init; }
    public Action? RegisterAssetTypes { get; init; }
    public Action? RegisterDocumentTypes { get; init; }
    public Action? Update { get; init; }
    public Action? UpdateUI { get; init; }
    public Action<PropertySet>? LoadUserSettings { get; init; }
    public Action<PropertySet>? SaveUserSettings { get; init; }
    public Action<List<Command>>? RegisterCommands { get; init; }
    public Assembly? ResourceAssembly { get; init; }
    public IEditorStore? Store { get; init; }
}

internal class EditorApplicationInstance : IApplication
{
    public void Update() => EditorApplication.Update();
    public void LateUpdate() => EditorApplication.LateUpdate();

    public void LoadConfig(ApplicationConfig config)
    {
        var props = PropertySetExtensions.LoadFile(EditorApplication.Store, ".noz/user.cfg");
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

    public void LoadAssets()
    {
        DocumentManager.UpdateExports();
        EditorAssets.LoadAssets();
        EditorApplication.PostLoad();
    }

    public void UnloadAssets() => EditorAssets.UnloadAssets();
    public void ReloadAssets() => EditorAssets.ReloadAssets();
}

public static partial class EditorApplication
{
    private static readonly Queue<Action> _mainThreadQueue = new();

    internal static EditorApplicationConfig AppConfig { get; private set; } = null!;

    public static IEditorStore Store { get; private set; } = null!;
    public static EditorConfig Config { get; private set; } = null!;
    public static string OutputPath { get; private set; } = null!;
    public static string EditorPath { get; private set; } = null!;
    public static string ProjectPath { get; private set; } = null!;
    private static bool _projectInitialized;

    public static void Run(EditorApplicationConfig config, string[] args)
    {
        AppConfig = config;
        var initProject = false;
        var exportOnly = false;
        var profilerMode = false;
        string? initProjectName = null;
        var clean = false;
        string? projectArg = null;
        string? editorPathArg = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--init")
            {
                initProject = true;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    initProjectName = args[++i];
            }
            else if (args[i] == "--export" || args[i] == "--import")
                exportOnly = true;
            else if (args[i] == "--profiler")
                profilerMode = true;
            else if (args[i] == "--project" && i + 1 < args.Length)
                projectArg = args[++i];
            else if (args[i] == "--editor-path" && i + 1 < args.Length)
                editorPathArg = args[++i];
            else if (args[i] == "--clean")
                clean = true;
        }

        // For CLI modes on Windows, attach to parent console so Console.WriteLine output is visible
        // (WinExe has no console by default; Linux/Mac don't need this)
        if ((exportOnly || initProject || config.Store != null) && OperatingSystem.IsWindows())
            AttachConsole(-1);

        // On iOS, skip filesystem walk — assets are embedded resources and
        // the store (GitStore) provides all project files.
        if (OperatingSystem.IsIOS())
        {
            var iosPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoZEditor");
            Directory.CreateDirectory(iosPath);
            Init(iosPath, projectArg ?? iosPath, clean, config);
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

        var projectPath = projectArg ?? editorPath;

        if (projectPath.StartsWith('.'))
        {
            projectPath = Path.Combine(Directory.GetCurrentDirectory(), projectPath);
            projectPath = Path.GetFullPath(projectPath);
        }

        // Handle project initialization before normal startup
        if (initProject)
        {
            try
            {
                var projectName = initProjectName ?? Path.GetFileName(Path.GetFullPath(projectPath));
                ProjectInitializer.Initialize(projectPath, projectName, editorPath);

                Log.Info("");
                Log.Info("Exporting assets...");
                Init(editorPath, projectPath, false, config);

                Log.Info($"Project '{projectName}' initialized successfully at {projectPath}");
                Log.Info("");
                Log.Info("Next steps:");
                Log.Info("  1. cd " + projectPath);
                Log.Info("  2. git init");
                Log.Info("  3. git submodule add https://github.com/nozgames/noz-cs noz");
                Log.Info("  4. git submodule update --init --recursive");
                Log.Info("  5. dotnet restore");
                Log.Info("  6. dotnet build");
                Log.Info("");
                Log.Info("To open in the editor:");
                Log.Info("  noz --project " + projectPath);
                return;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize project: {ex.Message}");
                Environment.Exit(1);
            }
        }

        // When a store is provided (e.g. GitStore), skip the directory walk —
        // the store already has the files at the project path.
        if (config.Store == null)
        {
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

        Init(editorPath, projectPath, clean, config);

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

        Application.Init(new ApplicationConfig
        {
            Title = config.Title,
            Width = 1600,
            Height = 900,
            HotReload = false,
            IconPath = config.IconPath ?? "res/windows/nozed.png",
            Platform = new SDLPlatform(),
            AudioBackend = new SDLAudioDriver(),
            Vtable = new EditorApplicationInstance(),
            AssetPath = Path.Combine(EditorPath, "library"),
            ResourceAssembly = config.ResourceAssembly,

            UI = new UIConfig()
            {
                DefaultFont = EditorAssets.Names.Seguisb,
                ScaleMode = UIScaleMode.ConstantPixelSize,
            },
            Graphics = new GraphicsConfig
            {
                Driver = new WebGPUGraphicsDriver(),
                PixelsPerUnit = Config?.PixelsPerUnit ?? 256,
                Vsync = true,
                HDR = true
            }
        });

        Application.Run();

        // On iOS, Run() returns immediately — CADisplayLink drives frames.
        // Shutdown will be called when the app terminates.
        if (OperatingSystem.IsIOS())
            return;

        Shutdown();
        Application.Shutdown();
    }

    private static bool _clean;

    private static void Init(string editorPath, string projectPath, bool clean, EditorApplicationConfig config)
    {
        EditorPath = editorPath;
        ProjectPath = projectPath;
        _clean = clean;
        _projectInitialized = false;

        Store = AppConfig.Store ?? new LocalStore();
        Store.Init(projectPath);

        // Register asset/document types (static registrations, no files needed)
        Application.RegisterAssetTypes();
        config.RegisterAssetTypes?.Invoke();

        AtlasDocument.RegisterDef();
        ShaderDocument.RegisterDef();
        SoundDocument.RegisterDef();
        SpriteDocument.RegisterDef();
        GenerationConfig.RegisterDef();
        FontDocument.RegisterDef();
        SkeletonDocument.RegisterDef();
        AnimationDocument.RegisterDef();
        VfxDocument.RegisterDef();
        BinDocument.RegisterDef();
        BundleDocument.RegisterDef();
        PaletteDocument.RegisterDef();

        config.RegisterDocumentTypes?.Invoke();

        // If store is ready, initialize the project now
        if (Store.IsReady)
            InitProject();
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
        DocumentManager.Init(Config.SourcePaths, Config.OutputPath);
        PaletteManager.Init();
        DocumentManager.LoadAll();
        PaletteManager.DiscoverPalettes();
        AtlasManager.Init();
        DocumentManager.InitExports(_clean);
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

        if (Config == null)
            return;

        DocumentManager.PostLoad();
        AtlasManager.RebuildTextureArray();
        VfxSystem.Shader = EditorAssets.Shaders.Sprite;
        Workspace.Init();
        UserSettings.Load();

        DocumentManager.SaveAll();
        DocumentManager.QueueGenerations();
    }

    private static void Shutdown()
    {
        if (_projectInitialized)
        {
            UserSettings.Save();
            DocumentManager.SaveAll();

            Workspace.Shutdown();
            ConfirmDialog.Shutdown();
            PopupMenu.Shutdown();
            EditorStyle.Shutdown();
            CollectionManager.Shutdown();
            PaletteManager.Shutdown();
            DocumentManager.Shutdown();
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
                DocumentManager.UpdateExports();
                EditorAssets.ReloadAssets();

                // Project-specific PostLoad (UI infra already initialized)
                DocumentManager.PostLoad();
                AtlasManager.RebuildTextureArray();
                VfxSystem.Shader = EditorAssets.Shaders.Sprite;
                Workspace.Init();
                UserSettings.Load();
                DocumentManager.SaveAll();
                DocumentManager.QueueGenerations();
            }
        }

        if (Config == null)
            return;

        if (!Application.HasFocus && !(Workspace.ActiveEditor is { RunInBackground: true}))
            Thread.Sleep(1000 / 30);

        DocumentManager.UpdateExports();
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

    [LibraryImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial void AttachConsole(int dwProcessId);
}
