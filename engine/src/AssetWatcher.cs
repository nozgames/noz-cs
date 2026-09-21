//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Collections.Concurrent;

namespace NoZ;

internal static class AssetWatcher
{
    private static FileSystemWatcher? _watcher;
    private static readonly ConcurrentQueue<(string type, string name)> _reloadQueue = new();
    private static readonly Dictionary<(string type, string name), (long due, int attempts)> _pending = [];

    internal static void Init()
    {
        if (OperatingSystem.IsIOS() || OperatingSystem.IsBrowser())
            return;

        var path = Application.AssetPath;
        if (!Directory.Exists(path))
            return;

        _watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
        _watcher.Error += (_, e) =>
        {
            Log.Warning($"Asset watcher: {e.GetException().Message}; rescanning library.");
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) Enqueue(file);
            }
            catch (IOException error) { Log.Warning(error.Message); }
        };
    }

    internal static void Shutdown()
    {
        _watcher?.Dispose();
        _watcher = null;
        _reloadQueue.Clear();
        _pending.Clear();
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e)
        => Enqueue(e.FullPath);

    private static void Enqueue(string path)
    {
        var fullPath = Path.GetFullPath(path).Replace('\\', '/');
        var assetPath = Path.GetFullPath(Application.AssetPath).Replace('\\', '/').TrimEnd('/');

        if (!fullPath.StartsWith(assetPath + "/", StringComparison.OrdinalIgnoreCase))
            return;

        // Path relative to asset root: e.g. "shader/lit_sprite"
        var relative = fullPath[(assetPath.Length + 1)..];
        var sep = relative.IndexOf('/');
        if (sep < 0)
            return;

        var typeName = relative[..sep];
        var assetName = relative[(sep + 1)..];
        // Exporters publish by renaming a temporary file into place.
        if (assetName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return;

        _reloadQueue.Enqueue((typeName, assetName));
    }

    internal static void Update()
    {
        while (_reloadQueue.TryDequeue(out var entry))
            _pending[entry] = (Environment.TickCount64 + 150, 0);

        foreach (var (entry, state) in _pending.ToArray())
        {
            if (Environment.TickCount64 < state.due) continue;
            _pending.Remove(entry);
            var def = FindDefByName(entry.type);
            if (def == null)
                continue;
            var name = entry.name;
            if (def.Type == AssetType.Shader)
            {
                var extension = Graphics.Driver.ShaderExtension;
                if (!string.IsNullOrEmpty(extension))
                {
                    if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
                    name = name[..^extension.Length];
                }
                else if (Path.HasExtension(name)) continue;
            }
            else if (Path.HasExtension(name)) continue;

            try
            {
                Log.Info($"Hot reload: {entry.type}/{entry.name}");
                Asset.ReloadByName(def.Type, name);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                if (state.attempts < 8)
                    _pending[entry] = (Environment.TickCount64 + 250, state.attempts + 1);
                else Log.Warning($"Could not reload {entry.type}/{name}: {error.Message}");
            }
        }
    }

    private static AssetDef? FindDefByName(string typeName)
    {
        foreach (var def in Asset.GetAllDefs())
        {
            if (def.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                return def;
        }
        return null;
    }
}
