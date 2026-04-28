//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace NoZ.Editor;

public static class RemoteHost
{
    private class Client
    {
        public required WebSocket Socket;
        public required string Id;
    }

    private static string _root = "";
    private static string _projectName = "";
    private static string _serverId = "";
    private static readonly ConcurrentDictionary<string, Client> _clients = new();
    private static readonly ConcurrentDictionary<string, (string ClientId, DateTime When)> _recentWrites = new();

    public static async Task RunAsync(string projectPath, int port, CancellationToken ct)
    {
        _root = Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _projectName = Path.GetFileName(_root);
        _serverId = Guid.NewGuid().ToString("N");

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://+:{port}/");
        var boundTo = $"http://0.0.0.0:{port}/ (reachable from other devices on this LAN)";

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Console.WriteLine($"Failed to bind http://+:{port}/ — {ex.Message}");
            Console.WriteLine("On Windows, binding to all interfaces requires an admin-granted URL reservation:");
            Console.WriteLine($"  netsh http add urlacl url=http://+:{port}/ user=Everyone");
            Console.WriteLine("Falling back to localhost only (iPad on LAN will NOT be able to connect).");
            listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();
            boundTo = $"http://localhost:{port}/ (LOCALHOST ONLY — see message above)";
        }

        var syncPaths = ResolveSyncPaths();

        Console.WriteLine($"Stope remote file server");
        Console.WriteLine($"  project name : {_projectName}");
        Console.WriteLine($"  project path : {_root}");
        Console.WriteLine($"  serving : {boundTo}");
        Console.WriteLine($"  sync    : {string.Join(", ", syncPaths)}");
        Console.WriteLine($"  press Ctrl-C to stop.");

        using var watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };

        FileSystemEventHandler onChange = (_, e) => HandleWatcherEvent(e.FullPath, RemoteProtocol.OpChanged);
        FileSystemEventHandler onCreated = (_, e) => HandleWatcherEvent(e.FullPath, RemoteProtocol.OpChanged);
        FileSystemEventHandler onDeleted = (_, e) => HandleWatcherEvent(e.FullPath, RemoteProtocol.OpDeleted);
        RenamedEventHandler onRenamed = (_, e) =>
        {
            HandleWatcherEvent(e.OldFullPath, RemoteProtocol.OpDeleted);
            HandleWatcherEvent(e.FullPath, RemoteProtocol.OpChanged);
        };

        watcher.Changed += onChange;
        watcher.Created += onCreated;
        watcher.Deleted += onDeleted;
        watcher.Renamed += onRenamed;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); } catch { }
            try { listener.Stop(); } catch { }
        }, CancellationToken.None);

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }

            _ = Task.Run(() => HandleRequestAsync(context, ct), ct);
        }

        foreach (var kvp in _clients)
        {
            try { kvp.Value.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).Wait(500); } catch { }
            try { kvp.Value.Socket.Dispose(); } catch { }
        }

        try { listener.Stop(); listener.Close(); } catch { }
        Console.WriteLine("Server stopped.");
    }

    private static string[] ResolveSyncPaths()
    {
        var cfg = Path.Combine(_root, "editor.cfg");
        if (File.Exists(cfg))
        {
            var props = PropertySet.Load(File.ReadAllText(cfg));
            if (props != null)
            {
                var paths = props.GetKeys("source").ToArray();
                if (paths.Length > 0)
                    return paths;
            }
        }
        return ["assets"];
    }

    private static void HandleWatcherEvent(string absolutePath, string op)
    {
        var rel = ToRelative(absolutePath);
        if (rel == null) return;
        if (rel.StartsWith(".noz/")) return;

        string? excludeClientId = null;
        if (_recentWrites.TryGetValue(rel, out var hint) && (DateTime.UtcNow - hint.When).TotalMilliseconds < 2000)
        {
            excludeClientId = hint.ClientId;
            _recentWrites.TryRemove(rel, out _);
        }

        long mtime = 0, size = 0;
        if (op == RemoteProtocol.OpChanged && File.Exists(absolutePath))
        {
            try
            {
                var info = new FileInfo(absolutePath);
                mtime = info.LastWriteTimeUtc.Ticks;
                size = info.Length;
            }
            catch { }
        }

        var evt = new EventDto { Op = op, Path = rel, MtimeTicks = mtime, Size = size };
        var json = JsonSerializer.Serialize(evt, RemoteJsonContext.Default.EventDto);
        var bytes = Encoding.UTF8.GetBytes(json);

        foreach (var kvp in _clients)
        {
            if (kvp.Key == excludeClientId) continue;
            if (kvp.Value.Socket.State != WebSocketState.Open) continue;
            _ = kvp.Value.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }

    private static string? ToRelative(string absolutePath)
    {
        var rootWithSep = _root + Path.DirectorySeparatorChar;
        if (absolutePath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            return absolutePath[rootWithSep.Length..].Replace('\\', '/');
        if (string.Equals(absolutePath, _root, StringComparison.OrdinalIgnoreCase))
            return "";
        return null;
    }

    private static async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            var req = context.Request;
            var resp = context.Response;
            var path = req.Url?.AbsolutePath ?? "/";
            var query = req.Url != null ? System.Web.HttpUtility.ParseQueryString(req.Url.Query) : null;
            var method = req.HttpMethod;
            var clientId = req.Headers[RemoteProtocol.HeaderClientId];

            switch (path)
            {
                case "/info":
                    await HandleInfoAsync(resp);
                    break;

                case "/list":
                    await HandleListAsync(resp, query?["path"] ?? "", query?["recursive"] == "1");
                    break;

                case "/file" when method == "HEAD":
                    HandleFileHead(resp, query?["path"] ?? "");
                    break;

                case "/file" when method == "GET":
                    await HandleFileGetAsync(resp, query?["path"] ?? "");
                    break;

                case "/file" when method == "PUT":
                    await HandleFilePutAsync(req, resp, query?["path"] ?? "", clientId);
                    break;

                case "/file" when method == "DELETE":
                    HandleFileDelete(resp, query?["path"] ?? "", clientId);
                    break;

                case "/move" when method == "POST":
                    HandleMove(resp, query?["src"] ?? "", query?["dst"] ?? "", clientId);
                    break;

                case "/mkdir" when method == "POST":
                    HandleMkdir(resp, query?["path"] ?? "");
                    break;

                case "/events" when req.IsWebSocketRequest:
                    await HandleWebSocketAsync(context, ct);
                    return;

                default:
                    resp.StatusCode = 404;
                    break;
            }

            resp.Close();
        }
        catch (Exception ex)
        {
            Log.Error($"Request error: {ex.Message}");
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch { }
        }
    }

    private static async Task HandleInfoAsync(HttpListenerResponse resp)
    {
        var dto = new InfoResponseDto
        {
            Root = _root,
            ProjectName = _projectName,
            SyncPaths = ResolveSyncPaths(),
            ServerId = _serverId,
            Protocol = RemoteProtocol.Version,
        };
        var json = JsonSerializer.Serialize(dto, RemoteJsonContext.Default.InfoResponseDto);
        await WriteJsonAsync(resp, json);
    }

    private static async Task HandleListAsync(HttpListenerResponse resp, string relPath, bool recursive)
    {
        if (!IsSafePath(relPath))
        {
            resp.StatusCode = 400;
            return;
        }

        var fullPath = string.IsNullOrEmpty(relPath) ? _root : Path.Combine(_root, relPath);
        var dto = new ListResponseDto();

        if (Directory.Exists(fullPath))
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", option))
            {
                var relative = Path.GetRelativePath(_root, file).Replace('\\', '/');
                if (relative.StartsWith(".noz/")) continue;
                var info = new FileInfo(file);
                dto.Files.Add(new FileEntryDto
                {
                    Path = relative,
                    MtimeTicks = info.LastWriteTimeUtc.Ticks,
                    Size = info.Length,
                });
            }
        }

        var json = JsonSerializer.Serialize(dto, RemoteJsonContext.Default.ListResponseDto);
        await WriteJsonAsync(resp, json);
    }

    private static void HandleFileHead(HttpListenerResponse resp, string relPath)
    {
        if (!IsSafePath(relPath) || string.IsNullOrEmpty(relPath))
        {
            resp.StatusCode = 400;
            return;
        }

        var full = Path.Combine(_root, relPath);
        if (!File.Exists(full))
        {
            resp.StatusCode = 404;
            return;
        }

        var info = new FileInfo(full);
        resp.Headers[RemoteProtocol.HeaderMtime] = info.LastWriteTimeUtc.Ticks.ToString();
        resp.Headers[RemoteProtocol.HeaderSize] = info.Length.ToString();
        resp.ContentLength64 = info.Length;
        resp.StatusCode = 200;
    }

    private static async Task HandleFileGetAsync(HttpListenerResponse resp, string relPath)
    {
        if (!IsSafePath(relPath) || string.IsNullOrEmpty(relPath))
        {
            resp.StatusCode = 400;
            return;
        }

        var full = Path.Combine(_root, relPath);
        if (!File.Exists(full))
        {
            resp.StatusCode = 404;
            return;
        }

        var info = new FileInfo(full);
        resp.Headers[RemoteProtocol.HeaderMtime] = info.LastWriteTimeUtc.Ticks.ToString();
        resp.Headers[RemoteProtocol.HeaderSize] = info.Length.ToString();
        resp.ContentType = "application/octet-stream";
        resp.ContentLength64 = info.Length;

        using var fs = File.OpenRead(full);
        await fs.CopyToAsync(resp.OutputStream);
    }

    private static async Task HandleFilePutAsync(
        HttpListenerRequest req,
        HttpListenerResponse resp,
        string relPath,
        string? clientId)
    {
        if (!IsSafePath(relPath) || string.IsNullOrEmpty(relPath))
        {
            resp.StatusCode = 400;
            return;
        }

        var full = Path.Combine(_root, relPath);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (!string.IsNullOrEmpty(clientId))
            _recentWrites[relPath] = (clientId, DateTime.UtcNow);

        using (var fs = File.Create(full))
            await req.InputStream.CopyToAsync(fs);

        var info = new FileInfo(full);
        resp.Headers[RemoteProtocol.HeaderMtime] = info.LastWriteTimeUtc.Ticks.ToString();
        resp.Headers[RemoteProtocol.HeaderSize] = info.Length.ToString();
        resp.StatusCode = 200;
    }

    private static void HandleFileDelete(HttpListenerResponse resp, string relPath, string? clientId)
    {
        if (!IsSafePath(relPath) || string.IsNullOrEmpty(relPath))
        {
            resp.StatusCode = 400;
            return;
        }

        var full = Path.Combine(_root, relPath);
        if (!File.Exists(full))
        {
            resp.StatusCode = 404;
            return;
        }

        if (!string.IsNullOrEmpty(clientId))
            _recentWrites[relPath] = (clientId, DateTime.UtcNow);

        File.Delete(full);
        resp.StatusCode = 204;
    }

    private static void HandleMove(HttpListenerResponse resp, string src, string dst, string? clientId)
    {
        if (!IsSafePath(src) || !IsSafePath(dst) || string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst))
        {
            resp.StatusCode = 400;
            return;
        }

        var srcFull = Path.Combine(_root, src);
        var dstFull = Path.Combine(_root, dst);
        if (!File.Exists(srcFull))
        {
            resp.StatusCode = 404;
            return;
        }

        var dir = Path.GetDirectoryName(dstFull);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (!string.IsNullOrEmpty(clientId))
        {
            _recentWrites[src] = (clientId, DateTime.UtcNow);
            _recentWrites[dst] = (clientId, DateTime.UtcNow);
        }

        File.Move(srcFull, dstFull);
        resp.StatusCode = 204;
    }

    private static void HandleMkdir(HttpListenerResponse resp, string relPath)
    {
        if (!IsSafePath(relPath) || string.IsNullOrEmpty(relPath))
        {
            resp.StatusCode = 400;
            return;
        }

        Directory.CreateDirectory(Path.Combine(_root, relPath));
        resp.StatusCode = 204;
    }

    private static async Task HandleWebSocketAsync(HttpListenerContext context, CancellationToken ct)
    {
        var wsContext = await context.AcceptWebSocketAsync(null);
        var id = Guid.NewGuid().ToString("N");
        var client = new Client { Socket = wsContext.WebSocket, Id = id };
        _clients[id] = client;

        var hello = JsonSerializer.Serialize(new EventDto { Op = "hello", Path = id }, RemoteJsonContext.Default.EventDto);
        var helloBytes = Encoding.UTF8.GetBytes(hello);
        try { await wsContext.WebSocket.SendAsync(helloBytes, WebSocketMessageType.Text, true, ct); } catch { }

        var buffer = new byte[4096];
        try
        {
            while (wsContext.WebSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await wsContext.WebSocket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch { }
        finally
        {
            _clients.TryRemove(id, out _);
            try { wsContext.WebSocket.Dispose(); } catch { }
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse resp, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.ContentType = "application/json";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
    }

    private static bool IsSafePath(string path)
    {
        if (path == null) return false;
        if (path.Contains("..")) return false;
        if (Path.IsPathRooted(path)) return false;
        return true;
    }
}
