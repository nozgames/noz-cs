//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#if DEBUG

using System.Collections.Concurrent;

namespace NoZ;

internal static class AssetWatcher
{
    private static FileSystemWatcher? _watcher;
    private static readonly ConcurrentQueue<(string type, string name)> _reloadQueue = new();

    internal static void Init()
    {
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
    }

    internal static void Shutdown()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var fullPath = e.FullPath.Replace('\\', '/');
        var assetPath = Application.AssetPath.Replace('\\', '/');

        if (!fullPath.StartsWith(assetPath, StringComparison.OrdinalIgnoreCase))
            return;

        // Path relative to asset root: e.g. "shader/lit_sprite"
        var relative = fullPath[(assetPath.Length + 1)..];
        var sep = relative.IndexOf('/');
        if (sep < 0)
            return;

        var typeName = relative[..sep];
        var assetName = relative[(sep + 1)..];

        _reloadQueue.Enqueue((typeName, assetName));
    }

    internal static void Update()
    {
        while (_reloadQueue.TryDequeue(out var entry))
        {
            var def = FindDefByName(entry.type);
            if (def == null)
                continue;

            try
            {
                Log.Info($"Hot reload: {entry.type}/{entry.name}");
                Asset.ReloadByName(def.Type, entry.name);
            }
            catch (IOException)
            {
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

#else

namespace NoZ;

internal static class AssetWatcher
{
    internal static void Init() { }
    internal static void Shutdown() { }
    internal static void Update() { }
}

#endif
