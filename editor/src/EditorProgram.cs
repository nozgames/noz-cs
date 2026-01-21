//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Reflection;
using NoZ;
using NoZ.Platform;
using NoZ.Editor;

// Find the editor root by walking up from the executable location
var initialDir = Directory.GetCurrentDirectory();
Log.Info($"Initial working directory: {initialDir}");

if (Directory.Exists(Path.Combine(initialDir, "library")))
{
    Log.Info($"Editor root: {initialDir}");
}
else
{
    Log.Info("Searching for editor root...");
    var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    var editorRoot = exeDir;
    while (editorRoot != null && !Directory.Exists(Path.Combine(editorRoot, "library")))
        editorRoot = Path.GetDirectoryName(editorRoot);

    if (editorRoot != null)
    {
        Directory.SetCurrentDirectory(editorRoot);
        Log.Info($"Editor root: {editorRoot}");
    }
    else
    {
        Log.Warning($"Could not find editor root (no 'library' folder found above {exeDir})");
    }
}

string? projectPath = null;
var clean = false;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--project" && i + 1 < args.Length)
        projectPath = args[++i];
    else if (args[i] == "--clean")
        clean = true;
}

EditorApplication.Init(projectPath, clean);

Application.Init(new ApplicationConfig
{
    Title = "NoZ Editor",
    Width = 1600,
    Height = 900,
    IconPath = "res/windows/nozed.png",
    Platform = new SDLPlatform(),
    AudioBackend = new SDLAudioDriver(),
    Vtable = new EditorVtable(),

    UI = new UIConfig()
    {
        DefaultFont = EditorAssets.Names.Seguisb
    },
    Graphics = new GraphicsConfig
    {
        //Driver = new DirectX12GraphicsDriver(),
        Driver = new OpenGLGraphicsDriver(),
        CompositeShader = "composite",
        PixelsPerUnit = EditorApplication.Config.PixelsPerUnit
    }
});

Application.Run();

EditorApplication.Shutdown();
Application.Shutdown();
