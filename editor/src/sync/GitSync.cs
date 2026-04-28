//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class GitSync : IProjectSync
{
    // Register at https://github.com/settings/developers → OAuth Apps
    public const string DefaultClientId = "Ov23ligSc1XSwyDdwSzU";

    private readonly string _projectPath;
    private readonly string _manifestPath;
    private readonly string _token;
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _branch;
    private readonly GitHubApi _api;

    private SyncManifest _manifest;
    private bool _syncing;
    private string[] _syncPaths = [];
    private HashSet<string> _pushablePaths = [];

    public string Name => "GitHub";
    public string ProjectPath => _projectPath;
    public bool IsSyncing => _syncing;

    public event Action? SyncCompleted;

    public GitSync(string token, string owner, string repo, string branch, string projectPath)
    {
        _token = token;
        _owner = owner;
        _repo = repo;
        _branch = branch;
        _projectPath = Path.GetFullPath(projectPath);
        _manifestPath = Path.Combine(_projectPath, ".noz", "sync.manifest");
        _api = new GitHubApi(_token);
        _manifest = SyncManifest.Load(_manifestPath);
    }

    public static string GetDefaultCachePath(string owner, string repo)
    {
        var baseDir = OperatingSystem.IsIOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoZEditor", "git")
            : Path.Combine(Path.GetTempPath(), "stope-git-client");
        var cacheDir = Path.Combine(baseDir, $"{owner}_{repo}");
        Directory.CreateDirectory(cacheDir);
        return cacheDir;
    }

    public async Task SyncAsync(CancellationToken ct = default)
    {
        _syncing = true;
        try
        {
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
        var gitRef = await _api.GetRefAsync(_owner, _repo, _branch, ct);
        var headSha = gitRef.Object.Sha;

        var editorCfgAbs = Absolute("editor.cfg");
        if (_manifest.RemoteCommitSha == headSha && File.Exists(editorCfgAbs))
        {
            Log.Info("Already up to date.");
            return;
        }

        var rootTree = await _api.GetTreeAsync(_owner, _repo, headSha, recursive: false, ct: ct);

        var editorCfgEntry = rootTree.Tree.FirstOrDefault(e => e.Type == "blob" && e.Path == "editor.cfg");
        if (editorCfgEntry != null && (_manifest.IsRemotelyModified("editor.cfg", editorCfgEntry.Sha) || !File.Exists(editorCfgAbs)))
        {
            var data = await _api.DownloadBlobAsync(_owner, _repo, _branch, "editor.cfg", ct);
            WriteFile("editor.cfg", data);
            var info = new FileInfo(editorCfgAbs);
            _manifest.SetEntry("editor.cfg", editorCfgEntry.Sha, info.LastWriteTimeUtc.Ticks, info.Length);
            Log.Info("Pulled editor.cfg");
        }

        ResolveSyncPaths();

        var submodules = await ParseGitmodulesAsync(rootTree, ct);

        var resolvedPaths = new List<string>();
        var toDownload = new List<(string RelativePath, string DownloadPath, string Sha, string Owner, string Repo, string Ref)>();

        foreach (var syncPath in _syncPaths)
        {
            var result = await _api.GetSubtreeAsync(_owner, _repo, headSha, syncPath, submodules, ct);
            if (result == null)
            {
                Log.Info($"Skipping '{syncPath}' (not found).");
                continue;
            }
            resolvedPaths.Add(syncPath);

            var subtree = result.Value;

            if (subtree.SubmoduleCommit == null)
                _pushablePaths.Add(syncPath);

            var blobOwner = subtree.SubmoduleOwner ?? _owner;
            var blobRepo = subtree.SubmoduleRepo ?? _repo;
            var blobRef = subtree.SubmoduleCommit ?? _branch;

            foreach (var entry in subtree.Tree.Tree.Where(e => e.Type == "blob"))
            {
                var relativePath = $"{syncPath}/{entry.Path}";

                if (!_manifest.IsRemotelyModified(relativePath, entry.Sha) && File.Exists(Absolute(relativePath)))
                    continue;

                var downloadPath = subtree.SubmoduleSubPath != null
                    ? $"{subtree.SubmoduleSubPath}/{entry.Path}"
                    : relativePath;

                toDownload.Add((relativePath, downloadPath, entry.Sha, blobOwner, blobRepo, blobRef));
            }
        }

        _syncPaths = resolvedPaths.ToArray();

        foreach (var (relativePath, _, _, _, _, _) in toDownload)
        {
            var dir = Path.GetDirectoryName(Absolute(relativePath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        await Parallel.ForEachAsync(toDownload, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = ct,
        }, async (item, token) =>
        {
            var (relativePath, downloadPath, sha, blobOwner, blobRepo, blobRef) = item;

            var data = await _api.DownloadBlobAsync(blobOwner, blobRepo, blobRef, downloadPath, token);
            WriteFile(relativePath, data);

            var fileInfo = new FileInfo(Absolute(relativePath));
            _manifest.SetEntry(relativePath, sha, fileInfo.LastWriteTimeUtc.Ticks, fileInfo.Length);
        });
        _manifest.RemoteCommitSha = headSha;

        if (toDownload.Count > 0)
            Log.Info($"Pulled {toDownload.Count} file(s).");
    }

    private async Task PushAsync(CancellationToken ct)
    {
        var modified = new List<string>();

        foreach (var syncPath in _pushablePaths)
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

        var treeEntries = new List<GitTreeCreateEntry>();
        foreach (var filePath in modified)
        {
            var data = File.ReadAllBytes(Absolute(filePath));
            var blobResponse = await _api.CreateBlobAsync(_owner, _repo, data, ct);
            treeEntries.Add(new GitTreeCreateEntry
            {
                Path = filePath.Replace('\\', '/'),
                Sha = blobResponse.Sha,
            });
        }

        var gitRef = await _api.GetRefAsync(_owner, _repo, _branch, ct);
        var parentSha = gitRef.Object.Sha;

        var currentTree = await _api.GetTreeAsync(_owner, _repo, parentSha, recursive: false, ct: ct);

        var treeRequest = new GitTreeCreateRequest
        {
            BaseTree = currentTree.Sha,
            Tree = treeEntries,
        };
        var newTree = await _api.CreateTreeAsync(_owner, _repo, treeRequest, ct);

        var commitRequest = new GitCommitCreateRequest
        {
            Message = "Update assets from NoZ Editor",
            Tree = newTree.Sha,
            Parents = [parentSha],
        };
        var commit = await _api.CreateCommitAsync(_owner, _repo, commitRequest, ct);

        await _api.UpdateRefAsync(_owner, _repo, _branch, commit.Sha, ct);

        _manifest.RemoteCommitSha = commit.Sha;
        foreach (var filePath in modified)
        {
            var info = new FileInfo(Absolute(filePath));
            var blobSha = treeEntries.First(e => e.Path == filePath.Replace('\\', '/')).Sha!;
            _manifest.SetEntry(filePath, blobSha, info.LastWriteTimeUtc.Ticks, info.Length);
        }

        Log.Info($"Pushed {modified.Count} file(s).");
    }

    private async Task<Dictionary<string, (string Owner, string Repo)>?> ParseGitmodulesAsync(
        GitTree rootTree, CancellationToken ct)
    {
        var entry = rootTree.Tree.FirstOrDefault(e => e.Type == "blob" && e.Path == ".gitmodules");
        if (entry == null)
            return null;

        var data = await _api.DownloadBlobAsync(_owner, _repo, _branch, ".gitmodules", ct);
        var content = System.Text.Encoding.UTF8.GetString(data);

        var submodules = new Dictionary<string, (string Owner, string Repo)>();
        string? currentPath = null;

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("path = "))
                currentPath = trimmed["path = ".Length..].Trim();
            else if (trimmed.StartsWith("url = ") && currentPath != null)
            {
                var url = trimmed["url = ".Length..].Trim();
                var parts = url.TrimEnd('/').Split('/');
                if (parts.Length >= 2)
                {
                    var repo = parts[^1].Replace(".git", "");
                    var owner = parts[^2];
                    if (owner.Contains(':'))
                        owner = owner.Split(':')[^1];
                    submodules[currentPath] = (owner, repo);
                }
                currentPath = null;
            }
        }

        return submodules.Count > 0 ? submodules : null;
    }

    private void ResolveSyncPaths()
    {
        var editorCfg = Absolute("editor.cfg");
        if (File.Exists(editorCfg))
        {
            var props = PropertySet.LoadFile(editorCfg);
            if (props != null)
            {
                _syncPaths = props.GetKeys("source").ToArray();
                return;
            }
        }

        _syncPaths = ["assets"];
    }

    private void WriteFile(string relativePath, byte[] data)
    {
        var abs = Absolute(relativePath);
        var dir = Path.GetDirectoryName(abs);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(abs, data);
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
        _api.Dispose();
    }
}
