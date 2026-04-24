//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public interface IEditorStore : IDisposable
{
    string Name { get; }
    bool IsRemote { get; }
    bool IsReady { get; }
    void UpdateUI();
    bool FileExists(string path);
    string ReadAllText(string path);
    byte[] ReadAllBytes(string path);
    void WriteAllText(string path, string contents);
    void WriteAllBytes(string path, byte[] data);
    void DeleteFile(string path);
    void MoveFile(string src, string dst);
    void CopyFile(string src, string dst);
    DateTime GetLastWriteTimeUtc(string path);
    Stream OpenRead(string path);
    Stream OpenWrite(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option);
    event Action<string>? FileChanged;
    void StartWatching(string path);
    void StopWatching();
    bool CanSync { get; }
    bool IsSyncing { get; }
    Task SyncAsync(CancellationToken ct = default);
    event Action? SyncCompleted;
    bool RequiresAuth { get; }
    bool IsAuthenticated { get; }
    Task<bool> LoginAsync(CancellationToken ct = default);
    void Logout();
    event Action? AuthStateChanged;
}
