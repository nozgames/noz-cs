//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class LocalStore : IEditorStore
{
    private readonly string _root = "";
    private readonly List<FileSystemWatcher> _watchers = [];

    public string Name => "Local";
    public bool IsRemote => false;
    public bool IsReady => true;
    public bool CanSync => false;
    public bool IsSyncing => false;
    public bool RequiresAuth => false;
    public bool IsAuthenticated => true;

    public event Action<string>? FileChanged;
#pragma warning disable CS0067
    public event Action? SyncCompleted;
    public event Action? AuthStateChanged;
#pragma warning restore CS0067

    public LocalStore(string rootPath)
    {
        _root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private string Resolve(string path) => Path.Combine(_root, path);

    private string ToRelative(string absolutePath)
    {
        if (absolutePath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            return absolutePath[_root.Length..].Replace('\\', '/');
        return absolutePath.Replace('\\', '/');
    }

    public bool FileExists(string path) => File.Exists(Resolve(path));
    public string ReadAllText(string path) => File.ReadAllText(Resolve(path));
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(Resolve(path));
    public void WriteAllText(string path, string contents) => File.WriteAllText(Resolve(path), contents);
    public void WriteAllBytes(string path, byte[] data) => File.WriteAllBytes(Resolve(path), data);
    public void DeleteFile(string path) => File.Delete(Resolve(path));
    public void MoveFile(string src, string dst) => File.Move(Resolve(src), Resolve(dst));
    public void CopyFile(string src, string dst) => File.Copy(Resolve(src), Resolve(dst), overwrite: true);
    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(Resolve(path));
    public Stream OpenRead(string path) => File.OpenRead(Resolve(path));
    public Stream OpenWrite(string path) => File.Create(Resolve(path));

    public bool DirectoryExists(string path) => Directory.Exists(Resolve(path));
    public void CreateDirectory(string path) => Directory.CreateDirectory(Resolve(path));

    public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option) =>
        Directory.EnumerateFiles(Resolve(path), pattern, option).Select(ToRelative);

    public void StartWatching(string path)
    {
        if (OperatingSystem.IsIOS())
            return;

        var fullPath = Resolve(path);
        var watcher = new FileSystemWatcher(fullPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        watcher.Changed += OnWatcherEvent;
        watcher.Created += OnWatcherEvent;
        watcher.Renamed += (_, e) => FileChanged?.Invoke(ToRelative(e.FullPath));
        _watchers.Add(watcher);
    }

    public void StopWatching()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e) =>
        FileChanged?.Invoke(ToRelative(e.FullPath));

    public void UpdateUI() { }
    public Task SyncAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> LoginAsync(CancellationToken ct = default) => Task.FromResult(true);
    public void Logout() { }

    public void Dispose()
    {
        StopWatching();
    }
}
