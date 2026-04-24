//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class GitStore : IEditorStore
{
    // Register at https://github.com/settings/developers → OAuth Apps
    public const string DefaultClientId = "Ov23ligSc1XSwyDdwSzU";

    private LocalStore _local = null!;
    private string _cachePath = "";
    private readonly string _manifestPath = ".noz/sync.manifest";

    private string? _token;
    private string? _owner;
    private string? _repo;
    private string _branch = "main";
    private readonly string _clientId;

    private enum SetupState { NeedAuth, WaitingForAuth, NeedRepo, LoadingRepos, Syncing, Ready }

    private SyncManifest _manifest = new();
    private GitHubApi? _api;
    private bool _syncing;
    private string[] _syncPaths = [];
    private HashSet<string> _pushablePaths = [];
    private SetupState _setupState = SetupState.NeedAuth;
    private DeviceCodeResponse? _deviceCode;
    private List<RepoInfo>? _repos;
    private string _syncStatus = "";
    private int _syncProgress;
    private int _syncTotal;
    private CancellationTokenSource? _syncCts;

    public string Name => "GitHub";
    public bool IsRemote => true;
    public bool IsReady => _setupState == SetupState.Ready;
    public bool CanSync => IsAuthenticated && _owner != null && _repo != null;
    public bool IsSyncing => _syncing;
    public bool RequiresAuth => true;
    public bool IsAuthenticated => _token != null;

    public event Action<string>? FileChanged;
    public event Action? SyncCompleted;
    public event Action? AuthStateChanged;

    public GitStore(string clientId, PropertySet props)
    {
        _clientId = clientId;
    }

    public static string GetDefaultCachePath()
    {
        var baseDir = OperatingSystem.IsIOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoZEditor", "git")
            : Path.Combine(Path.GetTempPath(), "stope-git-client");
        var cacheDir = Path.Combine(baseDir, "default");
        Directory.CreateDirectory(cacheDir);
        return cacheDir;
    }

    public void Init(string rootPath)
    {
        _cachePath = Path.GetFullPath(rootPath);
        _local = new LocalStore(rootPath);
        _local.FileChanged += path => FileChanged?.Invoke(path);
        LoadConfig();

        // Determine initial setup state
        if (!IsAuthenticated)
            _setupState = SetupState.NeedAuth;
        else if (_owner == null || _repo == null)
            _setupState = SetupState.NeedRepo;
        else if (!_local.FileExists("editor.cfg"))
            _setupState = SetupState.Syncing;
        else
            _setupState = SetupState.Ready;

        // Auto-start sync if we have credentials + repo but no files yet
        if (_setupState == SetupState.Syncing)
            BeginInitialSync();
    }

    public void UpdateUI()
    {
        using (UI.BeginColumn(new ContainerStyle
        {
            Width = Size.Percent(),
            Height = Size.Percent(),
            Align = Align.Center,
            Spacing = 16,
            Padding = new EdgeInsets(32, 32, 32, 32),
        }))
        {
            using (UI.BeginFlex()) { }

            switch (_setupState)
            {
                case SetupState.NeedAuth:
                    UI.Text("Connect to GitHub", EditorStyle.Text.Primary);
                    UI.Text("Sign in to sync your project assets.", EditorStyle.Text.Disabled);
                    if (UI.Button(WidgetIds.ConnectButton, "Connect to GitHub", EditorStyle.Button.Primary))
                        BeginLogin();
                    break;

                case SetupState.WaitingForAuth:
                    UI.Text("Enter this code on GitHub:", EditorStyle.Text.Primary);
                    UI.Text(_deviceCode?.UserCode ?? "...", EditorStyle.Text.Primary);
                    UI.Text("Waiting for authorization...", EditorStyle.Text.Disabled);
                    break;

                case SetupState.NeedRepo:
                    UI.Text("Select Repository", EditorStyle.Text.Primary);
                    if (UI.Button(WidgetIds.LoadReposButton, "Load Repositories", EditorStyle.Button.Primary))
                        BeginLoadRepos();
                    break;

                case SetupState.LoadingRepos:
                    UI.Text("Select Repository", EditorStyle.Text.Primary);
                    if (_repos == null)
                    {
                        UI.Text("Loading...", EditorStyle.Text.Disabled);
                    }
                    else
                    {
                        using (UI.BeginFlex())
                        using (UI.BeginScrollable(WidgetIds.RepoList))
                        {
                            var layout = new CollectionLayout { ItemHeight = EditorStyle.List.ItemHeight };
                            using (UI.BeginCollection(WidgetIds.RepoList, layout, _repos.Count, out var start, out var end))
                            {
                                for (var i = start; i < end; i++)
                                {
                                    var repo = _repos[i];
                                    using (UI.BeginRow(
                                        id: WidgetIds.RepoButton + i,
                                        style: EditorStyle.CommandPalette.Item))
                                    {
                                        UI.Text(repo.FullName, EditorStyle.Control.Text);
                                    }

                                    if (UI.WasPressed())
                                    {
                                        SetRepo(repo.Owner.Login, repo.Name, repo.DefaultBranch);
                                        BeginInitialSync();
                                    }
                                }
                            }
                        }
                    }
                    break;

                case SetupState.Syncing:
                    UI.Text("Syncing...", EditorStyle.Text.Primary);
                    UI.Text(_syncStatus, EditorStyle.Text.Disabled);
                    if (_syncTotal > 0)
                    {
                        var progress = (float)_syncProgress / _syncTotal;
                        using (UI.BeginRow(new ContainerStyle { Height = 4 }))
                        {
                            UI.Container(new ContainerStyle
                            {
                                Width = Size.Percent(progress),
                                Background = EditorStyle.Palette.Active,
                            });
                        }
                        UI.Text($"{_syncProgress} / {_syncTotal} files", EditorStyle.Text.Disabled);
                    }
                    if (UI.Button(WidgetIds.CancelButton, "Cancel", EditorStyle.Button.Secondary))
                        CancelSync();
                    break;
            }

            using (UI.BeginFlex()) { }
        }
    }

    private void BeginLogin()
    {
        _setupState = SetupState.WaitingForAuth;
        Task.Run(async () =>
        {
            _deviceCode = await GitHubApi.RequestDeviceCodeAsync(_clientId);
            Application.Platform?.OpenURL(_deviceCode.VerificationUri);

            EditorApplication.RunOnMainThread(() =>
                Log.Info($"Enter code: {_deviceCode.UserCode} at {_deviceCode.VerificationUri}"));

            while (true)
            {
                await Task.Delay(_deviceCode.Interval * 1000);
                var token = await GitHubApi.PollTokenAsync(_clientId, _deviceCode.DeviceCode);

                if (token.Error == "authorization_pending") continue;
                if (token.Error == "slow_down") { await Task.Delay(5000); continue; }
                if (token.Error == "expired_token")
                {
                    EditorApplication.RunOnMainThread(() =>
                    {
                        Log.Error("Code expired. Try again.");
                        _setupState = SetupState.NeedAuth;
                    });
                    return;
                }

                if (!string.IsNullOrEmpty(token.AccessToken))
                {
                    EditorApplication.RunOnMainThread(() =>
                    {
                        _token = token.AccessToken;
                        _api = new GitHubApi(_token);
                        SaveConfig();
                        AuthStateChanged?.Invoke();
                        Log.Info("Authenticated.");

                        if (_owner != null && _repo != null)
                            BeginInitialSync();
                        else
                            _setupState = SetupState.NeedRepo;
                    });
                    return;
                }

                EditorApplication.RunOnMainThread(() =>
                {
                    Log.Error($"Auth error: {token.Error}");
                    _setupState = SetupState.NeedAuth;
                });
                return;
            }
        });
    }

    private void BeginLoadRepos()
    {
        _setupState = SetupState.LoadingRepos;
        _repos = null;
        Task.Run(async () =>
        {
            var repos = await _api!.GetReposAsync();
            EditorApplication.RunOnMainThread(() => _repos = repos);
        });
    }

    private void BeginInitialSync()
    {
        _setupState = SetupState.Syncing;
        _syncStatus = "Starting...";
        _syncProgress = 0;
        _syncTotal = 0;
        _syncCts = new CancellationTokenSource();
        var ct = _syncCts.Token;
        Task.Run(async () =>
        {
            try
            {
                await SyncAsync(ct);
                EditorApplication.RunOnMainThread(() =>
                {
                    _setupState = SetupState.Ready;
                    Log.Info("Initial sync complete.");
                });
            }
            catch (OperationCanceledException)
            {
                EditorApplication.RunOnMainThread(() =>
                {
                    Log.Info("Sync cancelled.");
                    _setupState = _owner != null ? SetupState.NeedRepo : SetupState.NeedAuth;
                });
            }
            catch (Exception ex)
            {
                EditorApplication.RunOnMainThread(() =>
                {
                    Log.Error($"Sync failed: {ex.Message}");
                    _setupState = SetupState.NeedRepo;
                });
            }
        }, ct);
    }

    private void CancelSync()
    {
        _syncCts?.Cancel();
    }

    private static partial class WidgetIds
    {
        public static partial WidgetId ConnectButton { get; }
        public static partial WidgetId LoadReposButton { get; }
        public static partial WidgetId RepoList { get; }
        public static partial WidgetId RepoButton { get; }
        public static partial WidgetId CancelButton { get; }
    }

    // Delegate all file ops to inner LocalStore
    public bool FileExists(string path) => _local.FileExists(path);
    public string ReadAllText(string path) => _local.ReadAllText(path);
    public byte[] ReadAllBytes(string path) => _local.ReadAllBytes(path);
    public void WriteAllText(string path, string contents) => _local.WriteAllText(path, contents);
    public void WriteAllBytes(string path, byte[] data) => _local.WriteAllBytes(path, data);
    public void DeleteFile(string path) => _local.DeleteFile(path);
    public void MoveFile(string src, string dst) => _local.MoveFile(src, dst);
    public void CopyFile(string src, string dst) => _local.CopyFile(src, dst);
    public DateTime GetLastWriteTimeUtc(string path) => _local.GetLastWriteTimeUtc(path);
    public Stream OpenRead(string path) => _local.OpenRead(path);
    public Stream OpenWrite(string path) => _local.OpenWrite(path);
    public bool DirectoryExists(string path) => _local.DirectoryExists(path);
    public void CreateDirectory(string path) => _local.CreateDirectory(path);
    public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option) =>
        _local.EnumerateFiles(path, pattern, option);
    public void StartWatching(string path) => _local.StartWatching(path);
    public void StopWatching() => _local.StopWatching();

    public async Task<bool> LoginAsync(CancellationToken ct = default)
    {
        try
        {
            var deviceCode = await GitHubApi.RequestDeviceCodeAsync(_clientId, ct);

            Log.Info($"Go to: {deviceCode.VerificationUri}");
            Log.Info($"Enter code: {deviceCode.UserCode}");

            // Open browser
            Application.Platform?.OpenURL(deviceCode.VerificationUri);

            // Poll for token
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(deviceCode.Interval * 1000, ct);

                var tokenResponse = await GitHubApi.PollTokenAsync(_clientId, deviceCode.DeviceCode, ct);

                if (tokenResponse.Error == "authorization_pending")
                    continue;
                if (tokenResponse.Error == "slow_down")
                {
                    await Task.Delay(5000, ct);
                    continue;
                }
                if (tokenResponse.Error == "expired_token")
                {
                    Log.Error("Device code expired. Please try again.");
                    return false;
                }

                if (!string.IsNullOrEmpty(tokenResponse.AccessToken))
                {
                    _token = tokenResponse.AccessToken;
                    _api = new GitHubApi(_token);
                    SaveConfig();
                    AuthStateChanged?.Invoke();
                    Log.Info("GitHub authentication successful.");
                    return true;
                }

                Log.Error($"Auth error: {tokenResponse.Error}");
                return false;
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Login failed: {ex.Message}");
            return false;
        }
    }

    public void Logout()
    {
        _token = null;
        _api?.Dispose();
        _api = null;
        SaveConfig();
        AuthStateChanged?.Invoke();
    }

    public void SetRepo(string owner, string repo, string branch = "main")
    {
        _owner = owner;
        _repo = repo;
        _branch = branch;
        SaveConfig();
    }

    public async Task SyncAsync(CancellationToken ct = default)
    {
        if (!CanSync || _api == null || _owner == null || _repo == null)
            return;

        _syncing = true;
        try
        {
            await PullAsync(ct);

            // Save manifest after pull so push sees the correct state
            _manifest.Save(_local, _manifestPath);

            await PushAsync(ct);

            _manifest.LastSync = DateTime.UtcNow;
            _manifest.Save(_local, _manifestPath);
            SyncCompleted?.Invoke();

            Log.Info("Sync complete.");
        }
        catch (Exception ex)
        {
            Log.Error($"Sync failed: {ex.Message}");
        }
        finally
        {
            _syncing = false;
        }
    }

    private async Task PullAsync(CancellationToken ct)
    {
        // Get remote HEAD
        var gitRef = await _api!.GetRefAsync(_owner!, _repo!, _branch, ct);
        var headSha = gitRef.Object.Sha;

        // If we're at this commit AND editor.cfg exists, nothing to pull
        if (_manifest.RemoteCommitSha == headSha && _local.FileExists("editor.cfg"))
        {
            Log.Info("Already up to date.");
            return;
        }

        // Get top-level tree (non-recursive)
        var rootTree = await _api.GetTreeAsync(_owner!, _repo!, headSha, recursive: false, ct: ct);

        // Pull editor.cfg first
        var editorCfgEntry = rootTree.Tree.FirstOrDefault(e => e.Type == "blob" && e.Path == "editor.cfg");
        if (editorCfgEntry != null && (_manifest.IsRemotelyModified("editor.cfg", editorCfgEntry.Sha) || !_local.FileExists("editor.cfg")))
        {
            var data = await _api.DownloadBlobAsync(_owner!, _repo!, _branch, "editor.cfg", ct);
            _local.WriteAllBytes("editor.cfg", data);
            var info = new FileInfo(Path.Combine(_cachePath, "editor.cfg"));
            _manifest.SetEntry("editor.cfg", editorCfgEntry.Sha, info.LastWriteTimeUtc.Ticks, info.Length);
            Log.Info("Pulled editor.cfg");
        }

        // Parse [source] paths from editor.cfg
        ResolveSyncPaths();

        // Parse .gitmodules if present
        var submodules = await ParseGitmodulesAsync(rootTree, ct);

        // Collect all files to download across all source paths
        var resolvedPaths = new List<string>();
        var toDownload = new List<(string RelativePath, string DownloadPath, string Sha, string Owner, string Repo, string Ref)>();

        _syncStatus = "Scanning...";

        foreach (var syncPath in _syncPaths)
        {
            var result = await _api.GetSubtreeAsync(_owner!, _repo!, headSha, syncPath, submodules, ct);
            if (result == null)
            {
                Log.Info($"Skipping '{syncPath}' (not found).");
                continue;
            }
            resolvedPaths.Add(syncPath);

            var subtree = result.Value;

            if (subtree.SubmoduleCommit == null)
                _pushablePaths.Add(syncPath);

            var blobOwner = subtree.SubmoduleOwner ?? _owner!;
            var blobRepo = subtree.SubmoduleRepo ?? _repo!;
            var blobRef = subtree.SubmoduleCommit ?? _branch;

            foreach (var entry in subtree.Tree.Tree.Where(e => e.Type == "blob"))
            {
                var relativePath = $"{syncPath}/{entry.Path}";

                if (!_manifest.IsRemotelyModified(relativePath, entry.Sha) && _local.FileExists(relativePath))
                    continue;

                var downloadPath = subtree.SubmoduleSubPath != null
                    ? $"{subtree.SubmoduleSubPath}/{entry.Path}"
                    : relativePath;

                toDownload.Add((relativePath, downloadPath, entry.Sha, blobOwner, blobRepo, blobRef));
            }
        }

        _syncPaths = resolvedPaths.ToArray();
        _syncTotal = toDownload.Count;
        _syncProgress = 0;
        _syncStatus = "Downloading...";

        // Ensure all directories exist before parallel downloads
        foreach (var (relativePath, _, _, _, _, _) in toDownload)
        {
            var dir = Path.GetDirectoryName(relativePath);
            if (!string.IsNullOrEmpty(dir))
                _local.CreateDirectory(dir);
        }

        // Download in parallel batches
        await Parallel.ForEachAsync(toDownload, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = ct,
        }, async (item, token) =>
        {
            var (relativePath, downloadPath, sha, blobOwner, blobRepo, blobRef) = item;

            var data = await _api.DownloadBlobAsync(blobOwner, blobRepo, blobRef, downloadPath, token);
            _local.WriteAllBytes(relativePath, data);

            var fileInfo = new FileInfo(Path.Combine(_cachePath, relativePath));
            _manifest.SetEntry(relativePath, sha, fileInfo.LastWriteTimeUtc.Ticks, fileInfo.Length);
            Interlocked.Increment(ref _syncProgress);
        });
        _manifest.RemoteCommitSha = headSha;

        if (_syncTotal > 0)
            Log.Info($"Pulled {_syncTotal} file(s).");
    }

    private async Task PushAsync(CancellationToken ct)
    {
        // Find locally modified files
        var modified = new List<string>();

        foreach (var syncPath in _pushablePaths)
        {
            if (!_local.DirectoryExists(syncPath))
                continue;

            foreach (var filePath in _local.EnumerateFiles(syncPath, "*.*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(Path.Combine(_cachePath, filePath));
                if (_manifest.IsLocallyModified(filePath, info.LastWriteTimeUtc, info.Length))
                    modified.Add(filePath);
            }
        }

        // Also check editor.cfg
        if (_local.FileExists("editor.cfg"))
        {
            var info = new FileInfo(Path.Combine(_cachePath, "editor.cfg"));
            if (_manifest.IsLocallyModified("editor.cfg", info.LastWriteTimeUtc, info.Length))
                modified.Add("editor.cfg");
        }

        if (modified.Count == 0)
            return;

        // Create blobs
        var treeEntries = new List<GitTreeCreateEntry>();
        foreach (var filePath in modified)
        {
            var data = _local.ReadAllBytes(filePath);
            var blobResponse = await _api!.CreateBlobAsync(_owner!, _repo!, data, ct);
            treeEntries.Add(new GitTreeCreateEntry
            {
                Path = filePath.Replace('\\', '/'),
                Sha = blobResponse.Sha,
            });
        }

        // Get current HEAD for parent
        var gitRef = await _api!.GetRefAsync(_owner!, _repo!, _branch, ct);
        var parentSha = gitRef.Object.Sha;

        // Get top-level tree SHA for base_tree
        var currentTree = await _api.GetTreeAsync(_owner!, _repo!, parentSha, recursive: false, ct: ct);

        // Create new tree
        var treeRequest = new GitTreeCreateRequest
        {
            BaseTree = currentTree.Sha,
            Tree = treeEntries,
        };
        var newTree = await _api.CreateTreeAsync(_owner!, _repo!, treeRequest, ct);

        // Create commit
        var commitRequest = new GitCommitCreateRequest
        {
            Message = $"Update assets from NoZ Editor",
            Tree = newTree.Sha,
            Parents = [parentSha],
        };
        var commit = await _api.CreateCommitAsync(_owner!, _repo!, commitRequest, ct);

        // Update ref
        await _api.UpdateRefAsync(_owner!, _repo!, _branch, commit.Sha, ct);

        // Update manifest for pushed files
        _manifest.RemoteCommitSha = commit.Sha;
        foreach (var filePath in modified)
        {
            var info = new FileInfo(Path.Combine(_cachePath, filePath));
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

        var data = await _api!.DownloadBlobAsync(_owner!, _repo!, _branch, ".gitmodules", ct);
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
                // Parse owner/repo from GitHub URL (https://github.com/owner/repo or git@github.com:owner/repo)
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
        if (_local.FileExists("editor.cfg"))
        {
            var props = PropertySet.Load(_local.ReadAllText("editor.cfg"));
            if (props != null)
            {
                _syncPaths = props.GetKeys("source").ToArray();
                return;
            }
        }

        _syncPaths = ["assets"];
    }

    private void LoadConfig()
    {
        var configPath = Path.Combine(_cachePath, ".noz", "git.cfg");
        if (!File.Exists(configPath))
            return;

        var props = PropertySet.LoadFile(configPath);
        if (props == null)
            return;

        _token = props.GetString("auth", "token", "");
        if (string.IsNullOrEmpty(_token))
            _token = null;

        _owner = props.GetString("repo", "owner", "");
        if (string.IsNullOrEmpty(_owner))
            _owner = null;

        _repo = props.GetString("repo", "name", "");
        if (string.IsNullOrEmpty(_repo))
            _repo = null;

        _branch = props.GetString("repo", "branch", "main");

        if (_token != null)
            _api = new GitHubApi(_token);

        _manifest = SyncManifest.Load(_local, _manifestPath);
    }

    private void SaveConfig()
    {
        var configDir = Path.Combine(_cachePath, ".noz");
        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);

        var props = new PropertySet();
        if (_token != null)
            props.SetString("auth", "token", _token);
        if (_owner != null)
            props.SetString("repo", "owner", _owner);
        if (_repo != null)
            props.SetString("repo", "name", _repo);
        props.SetString("repo", "branch", _branch);

        props.Save(Path.Combine(configDir, "git.cfg"));
    }

    public void Dispose()
    {
        _local.Dispose();
        _api?.Dispose();
    }
}
