//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace NoZ.Editor;

public partial class RemoteStore : IEditorStore
{
    private LocalStore _local = null!;
    private string _cachePath = "";
    private readonly string _manifestPath = ".noz/sync.manifest";
    private readonly string _configPath = ".noz/remote.cfg";

    private string? _host;
    private int _port = RemoteProtocol.DefaultPort;
    private string _hostInput = "";
    private string _portInput = RemoteProtocol.DefaultPort.ToString();
    private readonly bool _preconfigured;

    private HttpClient? _http;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _wsCts;
    private string? _clientId;
    private string[] _syncPaths = [];

    private SyncManifest _manifest = new();
    private bool _syncing;
    private string _syncStatus = "";
    private int _syncProgress;
    private int _syncTotal;
    private CancellationTokenSource? _syncCts;

    private readonly ConcurrentQueue<WriteOp> _pendingWrites = new();
    private Task? _writeLoop;

    private enum SetupState { NeedConnection, Connecting, Syncing, Ready, Disconnected }
    private SetupState _setupState = SetupState.NeedConnection;

    private enum WriteOpKind { Put, Delete, Move }
    private record WriteOp(WriteOpKind Kind, string Path, string? DstPath);

    public string Name => "Remote";
    public bool IsRemote => true;
    public bool IsReady => _setupState == SetupState.Ready;
    public bool CanSync => !string.IsNullOrEmpty(_host);
    public bool IsSyncing => _syncing;
    public bool RequiresAuth => false;
    public bool IsAuthenticated => true;

    public event Action<string>? FileChanged;
    public event Action? SyncCompleted;
#pragma warning disable CS0067
    public event Action? AuthStateChanged;
#pragma warning restore CS0067

    public RemoteStore() { }

    public RemoteStore(string host, int port)
    {
        _host = host;
        _port = port;
        _hostInput = host;
        _portInput = port.ToString();
        _preconfigured = true;
    }

    public void Init(string rootPath)
    {
        _cachePath = Path.GetFullPath(rootPath);
        _local = new LocalStore();
        _local.Init(rootPath);
        _local.FileChanged += path => FileChanged?.Invoke(path);
        LoadConfig();

        if (string.IsNullOrEmpty(_host))
        {
            _setupState = SetupState.NeedConnection;
            return;
        }

        BeginConnect();
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
                case SetupState.NeedConnection:
                case SetupState.Disconnected:
                    UI.Text(_setupState == SetupState.Disconnected ? "Disconnected" : "Connect to Desktop", EditorStyle.Text.Primary);
                    UI.Text("Enter the host and port of a desktop editor running with --remote.", EditorStyle.Text.Disabled);

                    using (UI.BeginColumn(new ContainerStyle { Spacing = 8, Width = new Size(320) }))
                    {
                        UI.Text("Host", EditorStyle.Text.Disabled);
                        _hostInput = UI.TextInput(WidgetIds.HostInput, _hostInput, EditorStyle.TextInput, "192.168.1.42");

                        UI.Text("Port", EditorStyle.Text.Disabled);
                        _portInput = UI.TextInput(WidgetIds.PortInput, _portInput, EditorStyle.TextInput, RemoteProtocol.DefaultPort.ToString());
                    }

                    if (UI.Button(WidgetIds.ConnectButton, "Connect", EditorStyle.Button.Primary))
                    {
                        if (!int.TryParse(_portInput, out var p) || p <= 0 || p > 65535)
                            p = RemoteProtocol.DefaultPort;

                        if (!string.IsNullOrWhiteSpace(_hostInput))
                        {
                            _host = _hostInput.Trim();
                            _port = p;
                            SaveConfig();
                            BeginConnect();
                        }
                    }
                    break;

                case SetupState.Connecting:
                    UI.Text($"Connecting to {_host}:{_port}...", EditorStyle.Text.Primary);
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
                        _syncCts?.Cancel();
                    break;
            }

            using (UI.BeginFlex()) { }
        }
    }

    private void BeginConnect()
    {
        _setupState = SetupState.Connecting;
        _http?.Dispose();
        _http = new HttpClient { BaseAddress = new Uri($"http://{_host}:{_port}/") };

        Task.Run(async () =>
        {
            try
            {
                var info = await GetInfoAsync(CancellationToken.None);

                if (!string.IsNullOrEmpty(info.Root) &&
                    string.Equals(Path.GetFullPath(info.Root).TrimEnd('\\', '/'),
                                  _cachePath.TrimEnd('\\', '/'),
                                  StringComparison.OrdinalIgnoreCase))
                {
                    EditorApplication.RunOnMainThread(() =>
                    {
                        Log.Error($"Remote cache path '{_cachePath}' is the same as the server's project path. " +
                                  $"Pass --project with a different directory to isolate the client cache.");
                        _setupState = SetupState.NeedConnection;
                    });
                    return;
                }

                EditorApplication.RunOnMainThread(() =>
                {
                    _syncPaths = info.SyncPaths;
                    Log.Info($"Connected to {_host}:{_port} (protocol {info.Protocol}).");
                    BeginInitialSync();
                });
            }
            catch (Exception ex)
            {
                EditorApplication.RunOnMainThread(() =>
                {
                    Log.Error($"Connect failed: {ex.Message}");
                    _setupState = SetupState.NeedConnection;
                });
            }
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
                    StartWriteLoop();
                    StartWebSocket();
                });
            }
            catch (OperationCanceledException)
            {
                EditorApplication.RunOnMainThread(() =>
                {
                    Log.Info("Sync cancelled.");
                    _setupState = SetupState.NeedConnection;
                });
            }
            catch (Exception ex)
            {
                EditorApplication.RunOnMainThread(() =>
                {
                    Log.Error($"Sync failed: {ex.Message}");
                    _setupState = SetupState.NeedConnection;
                });
            }
        }, ct);
    }

    private static partial class WidgetIds
    {
        public static partial WidgetId HostInput { get; }
        public static partial WidgetId PortInput { get; }
        public static partial WidgetId ConnectButton { get; }
        public static partial WidgetId CancelButton { get; }
    }

    // File ops — delegate reads to local cache, queue writes for background upload
    public bool FileExists(string path) => _local.FileExists(path);
    public string ReadAllText(string path) => _local.ReadAllText(path);
    public byte[] ReadAllBytes(string path) => _local.ReadAllBytes(path);
    public DateTime GetLastWriteTimeUtc(string path) => _local.GetLastWriteTimeUtc(path);
    public Stream OpenRead(string path) => _local.OpenRead(path);
    public bool DirectoryExists(string path) => _local.DirectoryExists(path);
    public void CreateDirectory(string path) => _local.CreateDirectory(path);
    public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option) => _local.EnumerateFiles(path, pattern, option);
    public void StartWatching(string path) => _local.StartWatching(path);
    public void StopWatching() => _local.StopWatching();

    public void WriteAllText(string path, string contents)
    {
        _local.WriteAllText(path, contents);
        EnqueueUpload(path);
    }

    public void WriteAllBytes(string path, byte[] data)
    {
        _local.WriteAllBytes(path, data);
        EnqueueUpload(path);
    }

    public Stream OpenWrite(string path)
    {
        var stream = _local.OpenWrite(path);
        return new UploadOnCloseStream(stream, () => EnqueueUpload(path));
    }

    public void DeleteFile(string path)
    {
        _local.DeleteFile(path);
        _pendingWrites.Enqueue(new WriteOp(WriteOpKind.Delete, path, null));
    }

    public void MoveFile(string src, string dst)
    {
        _local.MoveFile(src, dst);
        _pendingWrites.Enqueue(new WriteOp(WriteOpKind.Move, src, dst));
    }

    public void CopyFile(string src, string dst)
    {
        _local.CopyFile(src, dst);
        EnqueueUpload(dst);
    }

    private void EnqueueUpload(string path)
    {
        if (IsNoSync(path)) return;
        _pendingWrites.Enqueue(new WriteOp(WriteOpKind.Put, path, null));
    }

    private static bool IsNoSync(string path) => path.Replace('\\', '/').StartsWith(".noz/");

    private sealed class UploadOnCloseStream(Stream inner, Action onClose) : Stream
    {
        private bool _closed;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing)
        {
            if (!_closed)
            {
                _closed = true;
                inner.Dispose();
                onClose();
            }
            base.Dispose(disposing);
        }
    }

    public Task<bool> LoginAsync(CancellationToken ct = default) => Task.FromResult(true);
    public void Logout() { }

    public async Task SyncAsync(CancellationToken ct = default)
    {
        if (_http == null || string.IsNullOrEmpty(_host))
            return;

        _syncing = true;
        try
        {
            // Refresh sync paths (server may have new editor.cfg source list)
            var info = await GetInfoAsync(ct);
            _syncPaths = info.SyncPaths;

            await PullAsync(ct);
            _manifest.Save(_local, _manifestPath);

            await PushAsync(ct);
            _manifest.LastSync = DateTime.UtcNow;
            _manifest.Save(_local, _manifestPath);

            SyncCompleted?.Invoke();
        }
        finally
        {
            _syncing = false;
        }
    }

    private async Task PullAsync(CancellationToken ct)
    {
        _syncStatus = "Scanning...";
        var toDownload = new List<(string Path, long Mtime, long Size)>();

        // Always fetch editor.cfg explicitly
        var cfgEntry = await HeadAsync("editor.cfg", ct);
        if (cfgEntry != null && (_manifest.IsRemotelyModified("editor.cfg", cfgEntry.Value.Mtime.ToString()) || !_local.FileExists("editor.cfg")))
            toDownload.Add(("editor.cfg", cfgEntry.Value.Mtime, cfgEntry.Value.Size));

        foreach (var syncPath in _syncPaths)
        {
            var list = await ListAsync(syncPath, recursive: true, ct);
            foreach (var entry in list.Files)
            {
                if (!_manifest.IsRemotelyModified(entry.Path, entry.MtimeTicks.ToString()) && _local.FileExists(entry.Path))
                    continue;
                toDownload.Add((entry.Path, entry.MtimeTicks, entry.Size));
            }
        }

        _syncTotal = toDownload.Count;
        _syncProgress = 0;
        _syncStatus = "Downloading...";

        foreach (var (path, _, _) in toDownload)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                _local.CreateDirectory(dir);
        }

        await Parallel.ForEachAsync(toDownload, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = ct,
        }, async (item, token) =>
        {
            var (path, serverMtime, size) = item;
            var data = await _http!.GetByteArrayAsync($"file?path={Uri.EscapeDataString(path)}", token);
            _local.WriteAllBytes(path, data);
            var info = new FileInfo(Path.Combine(_cachePath, path));
            _manifest.SetEntry(path, serverMtime.ToString(), info.LastWriteTimeUtc.Ticks, info.Length);
            Interlocked.Increment(ref _syncProgress);
        });

        if (toDownload.Count > 0)
            Log.Info($"Pulled {toDownload.Count} file(s).");
    }

    private async Task PushAsync(CancellationToken ct)
    {
        var modified = new List<string>();
        foreach (var syncPath in _syncPaths)
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

        if (_local.FileExists("editor.cfg"))
        {
            var info = new FileInfo(Path.Combine(_cachePath, "editor.cfg"));
            if (_manifest.IsLocallyModified("editor.cfg", info.LastWriteTimeUtc, info.Length))
                modified.Add("editor.cfg");
        }

        if (modified.Count == 0)
            return;

        _syncStatus = "Uploading...";

        foreach (var path in modified)
        {
            await UploadAsync(path, ct);
        }

        Log.Info($"Pushed {modified.Count} file(s).");
    }

    private async Task<(long Mtime, long Size)?> HeadAsync(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Head, $"file?path={Uri.EscapeDataString(path)}");
        using var resp = await _http!.SendAsync(req, ct);
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
        var json = await _http!.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize(json, RemoteJsonContext.Default.ListResponseDto) ?? new ListResponseDto();
    }

    private async Task<InfoResponseDto> GetInfoAsync(CancellationToken ct)
    {
        var json = await _http!.GetStringAsync("info", ct);
        return JsonSerializer.Deserialize(json, RemoteJsonContext.Default.InfoResponseDto) ?? new InfoResponseDto();
    }

    private async Task UploadAsync(string path, CancellationToken ct)
    {
        if (_http == null) return;
        var data = _local.ReadAllBytes(path);
        using var req = new HttpRequestMessage(HttpMethod.Put, $"file?path={Uri.EscapeDataString(path)}")
        {
            Content = new ByteArrayContent(data),
        };
        if (_clientId != null)
            req.Headers.TryAddWithoutValidation(RemoteProtocol.HeaderClientId, _clientId);

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        long serverMtime = 0;
        if (resp.Headers.TryGetValues(RemoteProtocol.HeaderMtime, out var mtimes))
            long.TryParse(mtimes.First(), out serverMtime);

        var info = new FileInfo(Path.Combine(_cachePath, path));
        _manifest.SetEntry(path, serverMtime.ToString(), info.LastWriteTimeUtc.Ticks, info.Length);
    }

    private async Task DeleteRemoteAsync(string path, CancellationToken ct)
    {
        if (_http == null) return;
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"file?path={Uri.EscapeDataString(path)}");
        if (_clientId != null)
            req.Headers.TryAddWithoutValidation(RemoteProtocol.HeaderClientId, _clientId);
        using var resp = await _http.SendAsync(req, ct);
        _manifest.RemoveEntry(path);
    }

    private async Task MoveRemoteAsync(string src, string dst, CancellationToken ct)
    {
        if (_http == null) return;
        using var req = new HttpRequestMessage(HttpMethod.Post, $"move?src={Uri.EscapeDataString(src)}&dst={Uri.EscapeDataString(dst)}");
        if (_clientId != null)
            req.Headers.TryAddWithoutValidation(RemoteProtocol.HeaderClientId, _clientId);
        using var resp = await _http.SendAsync(req, ct);
    }

    private void StartWriteLoop()
    {
        if (_writeLoop != null) return;
        _writeLoop = Task.Run(async () =>
        {
            while (true)
            {
                if (!_pendingWrites.TryDequeue(out var op))
                {
                    await Task.Delay(50);
                    continue;
                }

                try
                {
                    switch (op.Kind)
                    {
                        case WriteOpKind.Put: await UploadAsync(op.Path, CancellationToken.None); break;
                        case WriteOpKind.Delete: await DeleteRemoteAsync(op.Path, CancellationToken.None); break;
                        case WriteOpKind.Move: await MoveRemoteAsync(op.Path, op.DstPath!, CancellationToken.None); break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Upload failed ({op.Kind} {op.Path}): {ex.Message}");
                    await Task.Delay(1000);
                }
            }
        });
    }

    private void StartWebSocket()
    {
        _wsCts = new CancellationTokenSource();
        _ = Task.Run(() => WebSocketLoopAsync(_wsCts.Token));
    }

    private async Task WebSocketLoopAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri($"ws://{_host}:{_port}/events"), ct);
                delay = TimeSpan.FromSeconds(1);
                await ReceiveLoopAsync(_ws, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Error($"WebSocket: {ex.Message}");
            }

            try { _ws?.Dispose(); } catch { }
            _ws = null;

            if (ct.IsCancellationRequested) break;
            await Task.Delay(delay, ct);
            delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));

            // Resync on reconnect in case we missed events
            try { await SyncAsync(ct); } catch { }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            sb.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            var json = sb.ToString();
            EventDto? evt;
            try { evt = JsonSerializer.Deserialize(json, RemoteJsonContext.Default.EventDto); }
            catch { continue; }
            if (evt == null) continue;

            if (evt.Op == "hello")
            {
                _clientId = evt.Path;
                Log.Info($"Remote client id: {_clientId}");
                continue;
            }

            await HandleServerEventAsync(evt, ct);
        }
    }

    private async Task HandleServerEventAsync(EventDto evt, CancellationToken ct)
    {
        if (evt.Op == RemoteProtocol.OpChanged)
        {
            if (!_manifest.IsRemotelyModified(evt.Path, evt.MtimeTicks.ToString()) && _local.FileExists(evt.Path))
                return;

            try
            {
                var data = await _http!.GetByteArrayAsync($"file?path={Uri.EscapeDataString(evt.Path)}", ct);
                var dir = Path.GetDirectoryName(evt.Path);
                if (!string.IsNullOrEmpty(dir))
                    _local.CreateDirectory(dir);
                _local.WriteAllBytes(evt.Path, data);
                var info = new FileInfo(Path.Combine(_cachePath, evt.Path));
                _manifest.SetEntry(evt.Path, evt.MtimeTicks.ToString(), info.LastWriteTimeUtc.Ticks, info.Length);
            }
            catch (Exception ex)
            {
                Log.Error($"Pull {evt.Path}: {ex.Message}");
            }
        }
        else if (evt.Op == RemoteProtocol.OpDeleted)
        {
            if (_local.FileExists(evt.Path))
            {
                try { _local.DeleteFile(evt.Path); } catch { }
            }
            _manifest.RemoveEntry(evt.Path);
        }
    }

    private void LoadConfig()
    {
        _manifest = SyncManifest.Load(_local, _manifestPath);

        if (_preconfigured)
            return;

        var full = Path.Combine(_cachePath, _configPath);
        if (!File.Exists(full))
            return;

        var props = PropertySet.LoadFile(full);
        if (props == null)
            return;

        var h = props.GetString("server", "host", "");
        if (!string.IsNullOrEmpty(h)) _host = h;
        _port = props.GetInt("server", "port", RemoteProtocol.DefaultPort);
        _hostInput = _host ?? "";
        _portInput = _port.ToString();
    }

    private void SaveConfig()
    {
        var dir = Path.Combine(_cachePath, ".noz");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var props = new PropertySet();
        if (_host != null) props.SetString("server", "host", _host);
        props.SetInt("server", "port", _port);
        props.Save(Path.Combine(_cachePath, _configPath));
    }

    public void Dispose()
    {
        _wsCts?.Cancel();
        try { _ws?.Dispose(); } catch { }
        _http?.Dispose();
        _local?.Dispose();
    }
}
