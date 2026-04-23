//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NoZ.Editor;

IEditorStore? store = null;
int? remotePort = null;
string? projectArg = null;
string? remoteConnectHost = null;
var remoteConnectPort = RemoteProtocol.DefaultPort;
var forwardArgs = new List<string>();

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--git")
    {
        store = new GitStore(GitStore.DefaultClientId);
        forwardArgs.Add(args[i]);
    }
    else if (args[i] == "--remote-connect" && i + 1 < args.Length)
    {
        var spec = args[++i];
        var colon = spec.LastIndexOf(':');
        if (colon > 0 && int.TryParse(spec[(colon + 1)..], out var p))
        {
            remoteConnectHost = spec[..colon];
            remoteConnectPort = p;
        }
        else
        {
            remoteConnectHost = spec;
        }
    }
    else if (args[i] == "--remote")
    {
        remotePort = RemoteProtocol.DefaultPort;
        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
        {
            remotePort = p;
            i++;
        }
    }
    else if (args[i] == "--project" && i + 1 < args.Length)
    {
        projectArg = args[++i];
        forwardArgs.Add("--project");
        forwardArgs.Add(projectArg);
    }
    else
    {
        forwardArgs.Add(args[i]);
    }
}

// Headless remote file server
if (remotePort != null)
{
    // This is a WinExe (no console subsystem). Attach to the parent console
    // so Console.WriteLine prints and Ctrl-C is delivered via CancelKeyPress.
    if (OperatingSystem.IsWindows())
        AttachParentConsole();

    var root = Path.GetFullPath(projectArg ?? Environment.CurrentDirectory);
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    await RemoteHost.RunAsync(root, remotePort.Value, cts.Token);
    return;
}

// Desktop remote client (for local testing or a second machine editing a remote project)
if (remoteConnectHost != null)
{
    store = new RemoteStore(remoteConnectHost, remoteConnectPort);

    // Auto-isolate cache from the server's project dir when --project wasn't given.
    if (projectArg == null)
    {
        var safeHost = remoteConnectHost.Replace(':', '_').Replace('.', '_');
        var cacheDir = Path.Combine(Path.GetTempPath(), "stope-remote-client", $"{safeHost}_{remoteConnectPort}");
        Directory.CreateDirectory(cacheDir);
        Console.WriteLine($"Remote client cache: {cacheDir}");
        forwardArgs.Add("--project");
        forwardArgs.Add(cacheDir);
    }
}

EditorApplication.Run(new EditorApplicationConfig
{
    Store = store,
}, forwardArgs.ToArray());

[SupportedOSPlatform("windows")]
static void AttachParentConsole()
{
    const int ATTACH_PARENT_PROCESS = -1;
    if (!AttachConsole(ATTACH_PARENT_PROCESS))
        AllocConsole();

    try
    {
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        Console.SetIn(new StreamReader(Console.OpenStandardInput()));
    }
    catch
    {
    }
}

[DllImport("kernel32.dll")]
[SupportedOSPlatform("windows")]
static extern bool AttachConsole(int dwProcessId);

[DllImport("kernel32.dll")]
[SupportedOSPlatform("windows")]
static extern bool AllocConsole();
