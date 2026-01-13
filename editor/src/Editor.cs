//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using noz.editor;

namespace noz;

internal static class Editor
{
    private static InputSet? _input;

    public static EditorConfig? Config { get; private set; }

    internal static void Init(string? projectPath = null, bool clean = false)
    {
        _input = new InputSet();
        Input.PushInputSet(_input);

        // Register document types
        TextureDocument.RegisterDef();

        Config = string.IsNullOrEmpty(projectPath)
            ? EditorConfig.FindAndLoad()
            : EditorConfig.Load(projectPath);

        if (Config == null)
        {
            Log.Warning("editor.cfg not found");
            return;
        }

        InitWorkspace(Config.SourcePaths, Config.OutputPath, clean);
    }

    internal static void InitWorkspace(string[] sourcePaths, string outputPath, bool clean = false)
    {
        DocumentManager.Init(sourcePaths, outputPath);
        Importer.Init(clean);

        if (Config != null)
            AssetManifest.Generate(Config);
    }

    internal static void Shutdown()
    {
        Importer.Shutdown();
        DocumentManager.Shutdown();
        _input = null;
        Config = null;
    }

    internal static void Update()
    {
    }
} 