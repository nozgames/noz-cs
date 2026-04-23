//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Collections.Concurrent;

namespace NoZ.Editor;

public class SyncManifest
{
    public string? RemoteCommitSha { get; set; }
    public string Branch { get; set; } = "main";
    public DateTime LastSync { get; set; }

    private readonly ConcurrentDictionary<string, FileEntry> _files = new();

    public struct FileEntry
    {
        public string BlobSha;
        public long ModifiedTicks;
        public long FileSize;
    }

    public void SetEntry(string path, string blobSha, long modifiedTicks, long fileSize)
    {
        _files[path] = new FileEntry
        {
            BlobSha = blobSha,
            ModifiedTicks = modifiedTicks,
            FileSize = fileSize,
        };
    }

    public bool TryGetEntry(string path, out FileEntry entry) =>
        _files.TryGetValue(path, out entry);

    public void RemoveEntry(string path) => _files.TryRemove(path, out _);

    public IEnumerable<string> Paths => _files.Keys;

    public bool IsLocallyModified(string path, DateTime mtime, long size)
    {
        if (!_files.TryGetValue(path, out var entry))
            return true; // new file
        return mtime.Ticks != entry.ModifiedTicks || size != entry.FileSize;
    }

    public bool IsRemotelyModified(string path, string remoteSha)
    {
        if (!_files.TryGetValue(path, out var entry))
            return true; // new file
        return entry.BlobSha != remoteSha;
    }

    public void Save(IEditorStore store, string path)
    {
        var props = new PropertySet();
        props.SetString("meta", "branch", Branch);
        props.SetString("meta", "last_sync", LastSync.ToString("O"));
        if (RemoteCommitSha != null)
            props.SetString("meta", "remote_sha", RemoteCommitSha);

        foreach (var (filePath, entry) in _files)
            props.SetString("files", filePath, $"{entry.BlobSha},{entry.ModifiedTicks},{entry.FileSize}");

        props.Save(path, store);
    }

    public static SyncManifest Load(IEditorStore store, string path)
    {
        var manifest = new SyncManifest();
        var props = PropertySetExtensions.LoadFile(store, path);
        if (props == null)
            return manifest;

        manifest.Branch = props.GetString("meta", "branch", "main");
        manifest.RemoteCommitSha = props.GetString("meta", "remote_sha", "");
        if (string.IsNullOrEmpty(manifest.RemoteCommitSha))
            manifest.RemoteCommitSha = null;

        var lastSync = props.GetString("meta", "last_sync", "");
        if (DateTime.TryParse(lastSync, out var dt))
            manifest.LastSync = dt;

        foreach (var key in props.GetKeys("files"))
        {
            var value = props.GetString("files", key, "");
            var parts = value.Split(',');
            if (parts.Length == 3 &&
                long.TryParse(parts[1], out var ticks) &&
                long.TryParse(parts[2], out var size))
            {
                manifest._files[key] = new FileEntry
                {
                    BlobSha = parts[0],
                    ModifiedTicks = ticks,
                    FileSize = size,
                };
            }
        }

        return manifest;
    }
}
