//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text.Json;

namespace NoZ.Editor;

public class RemoteSync : IProjectSync
{
    private readonly string _projectPath;
    private readonly string _manifestPath;
    private readonly string _host;
    private readonly int _port;
    private readonly HttpClient _http;

    private SyncManifest _manifest;
    private bool _syncing;
    private string[] _syncPaths = [];

    public string Name => "Remote";
    public string ProjectPath => _projectPath;
    public bool IsSyncing => _syncing;

    public event Action? SyncCompleted;

    private RemoteSync(string host, int port, HttpClient http, string projectPath, string[] syncPaths)
    {
        _host = host;
        _port = port;
        _http = http;
        _projectPath = projectPath;
        _manifestPath = Path.Combine(_projectPath, ".noz", "sync.manifest");
        _syncPaths = syncPaths;
        _manifest = SyncManifest.Load(_manifestPath);
    }

    public static async Task<RemoteSync> ConnectAsync(string host, int port, string cacheBaseDir, CancellationToken ct = default)
    {
        var http = new HttpClient { BaseAddress = new Uri($"http://{host}:{port}/") };
        InfoResponseDto info;
        try
        {
            var json = await http.GetStringAsync("info", ct);
            info = JsonSerializer.Deserialize(json, RemoteJsonContext.Default.InfoResponseDto) ?? new InfoResponseDto();
        }
        catch
        {
            http.Dispose();
            throw;
        }

        if (string.IsNullOrEmpty(info.ProjectName))
        {
            http.Dispose();
            throw new InvalidOperationException($"Remote server at {host}:{port} did not provide a project name.");
        }

        var projectPath = Path.GetFullPath(Path.Combine(cacheBaseDir, info.ProjectName));

        if (!string.IsNullOrEmpty(info.Root) &&
            string.Equals(Path.GetFullPath(info.Root).TrimEnd('\\', '/'),
                          projectPath.TrimEnd('\\', '/'),
                          StringComparison.OrdinalIgnoreCase))
        {
            http.Dispose();
            throw new InvalidOperationException(
                $"Remote cache path '{projectPath}' is the same as the server's project path. " +
                "Use a different cache directory to isolate the client.");
        }

        Directory.CreateDirectory(projectPath);
        return new RemoteSync(host, port, http, projectPath, info.SyncPaths);
    }

    public async Task SyncAsync(CancellationToken ct = default)
    {
        _syncing = true;
        try
        {
            var info = await GetInfoAsync(ct);
            _syncPaths = info.SyncPaths;

            await PullAsync(ct);
            _manifest.Save(_manifestPath);

            await PushAsync(ct);
            _manifest.LastSync = DateTime.UtcNow;
            _manifest.Save(_manifestPath);

            SyncCompleted?.Invoke();
            Log.Info("Sync complete.");
        }
        catch (Exception ex)
        {
            Log.Error($"Sync failed: {ex.Message}");
            throw;
        }
        finally
        {
            _syncing = false;
        }
    }

    private string Absolute(string relative) => Path.Combine(_projectPath, relative);

    private async Task PullAsync(CancellationToken ct)
    {
        var toDownload = new List<(string Path, long Mtime, long Size)>();

        var cfgEntry = await HeadAsync("editor.cfg", ct);
        if (cfgEntry != null && (_manifest.IsRemotelyModified("editor.cfg", cfgEntry.Value.Mtime.ToString()) || !File.Exists(Absolute("editor.cfg"))))
            toDownload.Add(("editor.cfg", cfgEntry.Value.Mtime, cfgEntry.Value.Size));

        foreach (var syncPath in _syncPaths)
        {
            var list = await ListAsync(syncPath, recursive: true, ct);
            foreach (var entry in list.Files)
            {
                if (!_manifest.IsRemotelyModified(entry.Path, entry.MtimeTicks.ToString()) && File.Exists(Absolute(entry.Path)))
                    continue;
                toDownload.Add((entry.Path, entry.MtimeTicks, entry.Size));
            }
        }

        foreach (var (path, _, _) in toDownload)
        {
            var dir = Path.GetDirectoryName(Absolute(path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        await Parallel.ForEachAsync(toDownload, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = ct,
        }, async (item, token) =>
        {
            var (path, serverMtime, _) = item;
            var data = await _http.GetByteArrayAsync($"file?path={Uri.EscapeDataString(path)}", token);
            var abs = Absolute(path);
            File.WriteAllBytes(abs, data);
            var info = new FileInfo(abs);
            _manifest.SetEntry(path, serverMtime.ToString(), info.LastWriteTimeUtc.Ticks, info.Length);
        });

        // Replay the FileSystemWatcher path for each pulled file so editor docs reload
        // and re-export queues update — needed on iOS where the OS watcher is disabled.
        foreach (var (path, _, _) in toDownload)
            Project.NotifyFilePulled(path);

        if (toDownload.Count > 0)
            Log.Info($"Pulled {toDownload.Count} file(s).");
    }

    private async Task PushAsync(CancellationToken ct)
    {
        var modified = new List<string>();
        foreach (var syncPath in _syncPaths)
        {
            var absSyncDir = Absolute(syncPath);
            if (!Directory.Exists(absSyncDir))
                continue;
            foreach (var absFilePath in Directory.EnumerateFiles(absSyncDir, "*.*", SearchOption.AllDirectories))
            {
                var relativePath = ToRelative(absFilePath);
                var info = new FileInfo(absFilePath);
                if (_manifest.IsLocallyModified(relativePath, info.LastWriteTimeUtc, info.Length))
                    modified.Add(relativePath);
            }
        }

        var editorCfgAbs = Absolute("editor.cfg");
        if (File.Exists(editorCfgAbs))
        {
            var info = new FileInfo(editorCfgAbs);
            if (_manifest.IsLocallyModified("editor.cfg", info.LastWriteTimeUtc, info.Length))
                modified.Add("editor.cfg");
        }

        if (modified.Count == 0)
            return;

        foreach (var path in modified)
            await UploadAsync(path, ct);

        Log.Info($"Pushed {modified.Count} file(s).");
    }

    private async Task<(long Mtime, long Size)?> HeadAsync(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Head, $"file?path={Uri.EscapeDataString(path)}");
        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) return null;
        if (!resp.Headers.TryGetValues(RemoteProtocol.HeaderMtime, out var mtimes)) return null;
        if (!resp.Headers.TryGetValues(RemoteProtocol.HeaderSize, out var sizes)) return null;
        if (!long.TryParse(mtimes.First(), out var m)) return null;
        if (!long.TryParse(sizes.First(), out var s)) return null;
        return (m, s);
    }

    private async Task<ListResponseDto> ListAsync(string path, bool recursive, CancellationToken ct)
    {
        var url = $"list?path={Uri.EscapeDataString(path)}&recursive={(recursive ? 1 : 0)}";
        var json = await _http.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize(json, RemoteJsonContext.Default.ListResponseDto) ?? new ListResponseDto();
    }

    private async Task<InfoResponseDto> GetInfoAsync(CancellationToken ct)
    {
        var json = await _http.GetStringAsync("info", ct);
        return JsonSerializer.Deserialize(json, RemoteJsonContext.Default.InfoResponseDto) ?? new InfoResponseDto();
    }

    private async Task UploadAsync(string relativePath, CancellationToken ct)
    {
        var abs = Absolute(relativePath);
        var data = File.ReadAllBytes(abs);
        using var req = new HttpRequestMessage(HttpMethod.Put, $"file?path={Uri.EscapeDataString(relativePath)}")
        {
            Content = new ByteArrayContent(data),
        };

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        long serverMtime = 0;
        if (resp.Headers.TryGetValues(RemoteProtocol.HeaderMtime, out var mtimes))
            long.TryParse(mtimes.First(), out serverMtime);

        var info = new FileInfo(abs);
        _manifest.SetEntry(relativePath, serverMtime.ToString(), info.LastWriteTimeUtc.Ticks, info.Length);
    }

    private string ToRelative(string absolutePath)
    {
        var root = _projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return absolutePath[root.Length..].Replace('\\', '/');
        return absolutePath.Replace('\\', '/');
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
