//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Collections.Concurrent;
using System.Net.Http;

namespace NoZ.Editor;

public static class DocumentManager
{
    private static readonly List<Document> _documents = [];
    private static readonly List<string> _sourcePaths = [];
    private static string _outputPath = "";
    private static bool _initialized;

    private static readonly Queue<Document> _exportQueue = [];
    private static readonly Queue<Document> _exportDeferred = [];
    private static readonly List<FileSystemWatcher> _watchers = [];
    private static readonly ConcurrentQueue<string> _watcherQueue = [];
    private static bool _watching;

    public static IReadOnlyList<Document> Documents => _documents;
    public static IReadOnlyList<string> SourcePaths => _sourcePaths;
    public static string OutputPath => _outputPath;

    public static IEnumerable<Document> SelectedDocuments =>
        _documents.Where(d => d.IsSelected);

    public delegate void DocumentAddedDelegate(Document doc);

    public static event DocumentAddedDelegate? DocumentAdded;
    public static event Action<Document>? OnExported;

    public static void NotifyDocumentAdded(Document doc) => DocumentAdded?.Invoke(doc);

    private static readonly Dictionary<AssetType, DocumentDef> _defsByType = new();
    private static readonly Dictionary<string, DocumentDef> _defsByExtension = new();

    public static int Count => _documents.Count;

    public static void Init(string[] sourcePaths, string outputPath)
    {
        _sourcePaths.Clear();
        _sourcePaths.AddRange(sourcePaths);
        _outputPath = outputPath;

        Log.Info($"DocumentOutputPath: {Path.GetFullPath(outputPath)}");
        foreach (var path in sourcePaths)
            Log.Info($"DocumentSourcePath: {Path.GetFullPath(path)}");

        Directory.CreateDirectory(outputPath);

        InitDocuments();
    }

    public static void InitExports(bool clean = false)
    {
        if (clean)
            Log.Info("Clean build requested, exporting all assets...");

        foreach (var doc in _documents)
            QueueExport(doc, clean);

        UpdateExports();

        foreach (var sourcePath in _sourcePaths)
            if (Directory.Exists(sourcePath))
                StartWatching(sourcePath);

        _watching = true;
    }

    public static void Shutdown()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();

        _initialized = false;
        _documents.Clear();
        _sourcePaths.Clear();
    }

    public static void RegisterDef(DocumentDef def)
    {
        _defsByType[def.Type] = def;
        foreach (var ext in def.Extensions)
            _defsByExtension[ext] = def;
    }

    public static DocumentDef? GetDef(AssetType type)
    {
        return _defsByType.TryGetValue(type, out var def) ? def : null;
    }

    public static DocumentDef? GetDef(string ext)
    {
        ext = ext.ToLowerInvariant();
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        return _defsByExtension.TryGetValue(ext, out var def) ? def : null;
    }

    public static IEnumerable<DocumentDef> GetCreatableDefs()
    {
        foreach (var def in _defsByType.Values)
            if (def.NewFile != null)
                yield return def;
    }

    public static Document? Create(string path)
    {
        string ext = Path.GetExtension(path);
        var def = GetDef(ext);
        if (def == null)
            return null;

        var doc = def.Factory();
        doc.Def = def;
        doc.Path = Path.GetFullPath(path).ToLowerInvariant();
        doc.Name = MakeCanonicalName(path);
        doc.Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
        _documents.Add(doc);
        return doc;
    }

    private static string GenerateUniqueName(AssetType assetType, string baseName)
    {
        var name = baseName;
        var index = 1;
        while (Find(assetType, name) != null)
        {
            name = $"{baseName}{index:D3}";
            index++;
        }
        return name;
    }

    public static Document? New(AssetType assetType, string? name, System.Numerics.Vector2? position = null)
    {
        var def = GetDef(assetType);
        if (def == null || def.NewFile == null)
            return null;

        if (_sourcePaths.Count == 0)
            return null;

        var typeName = (Asset.GetDef(assetType)?.Name ?? def.Name).ToLowerInvariant();
        name = name ?? MakeCanonicalName($"new_{typeName}");
        name = GenerateUniqueName(assetType, name);

        var canonicalName = MakeCanonicalName(name);
        if (Find(assetType, canonicalName) != null)
            return null;
        var path = Path.Combine(_sourcePaths[0], typeName, canonicalName + def.Extensions[0]);
        if (File.Exists(path))
            return null;

        var directory = Path.GetDirectoryName(path);
        if (directory != null)
            Directory.CreateDirectory(directory);

        var doc = Create(path);

        using (var writer = new StreamWriter(path))
            def.NewFile(writer);

        if (doc == null) return null;

        doc.Loaded = true;
        doc.Load();
        doc.LoadMetadata();

        if (position.HasValue)
        {
            doc.Position = position.Value;
            doc.IncrementVersion();
        }

        if (_initialized)
        {
            doc.PostLoad();
            doc.PostLoaded = true;
        }

        DocumentAdded?.Invoke(doc);

        AssetManifest.IsModified = true;

        return doc;
    }

    public static Document? Find(AssetType type, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        foreach (var doc in _documents)
        {
            if ((type == default || doc.Def.Type == type) && doc.Name == name)
                return doc;
        }
        return null;
    }

    public static void LoadAll()
    {
        foreach (var doc in _documents)
        {
            if (doc.Loaded) continue;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            doc.Loaded = true;
            doc.Load();
            var elapsed = sw.ElapsedMilliseconds;
            if (elapsed > 500)
                Log.Info($"Loaded {doc.Name} in {elapsed} ms");
        }

        // Load metadata after Load() so DiscoverFiles() has resolved the correct Path
        // (e.g. cheese.png -> cheese.sprite) before we read the .meta file.
        foreach (var doc in _documents)
            doc.LoadMetadata();
    }

    internal static void PostLoad()
    {
        foreach (var doc in _documents)
        {
            if (doc.Loaded && !doc.PostLoaded)
            {
                doc.PostLoaded = true;
                doc.PostLoad();
            }
        }

        _initialized = true;
    }

    private static void OnDocumentExported(Document doc)
    {
        if (!_initialized)
            return;

        if (!doc.Loaded)
        {
            doc.Loaded = true;
            doc.Load();
            doc.LoadMetadata();
            doc.PostLoad();
            doc.PostLoaded = true;
            NotifyDocumentAdded(doc);
        }
        else
        {
            doc.Reload();
        }
    }

    public static void SaveAll()
    {
        var count = 0;
        foreach (var doc in _documents)
        {
            if (!doc.IsModified)
                continue;

            if (doc.IsVisible)
                count++;

            doc.SilentExport = true;
            doc.Save();
            doc.SaveMetadata();
        }

        if (count > 0)
            Log.Info($"Saved {count} asset(s)");

        if (AssetManifest.IsModified)
            AssetManifest.Generate();
    }

    private static IEnumerable<string> GetCompanionFiles(Document doc)
    {
        var directory = Path.GetDirectoryName(doc.Path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(doc.Path);
        foreach (var ext in doc.Def.Extensions)
        {
            var path = Path.Combine(directory, stem + ext);
            if (File.Exists(path) && !string.Equals(path, doc.Path, StringComparison.OrdinalIgnoreCase))
                yield return path;
        }
    }

    public static bool Rename(Document doc, string name)
    {
        var canonicalName = MakeCanonicalName(name);
        if (Find(doc.Def.Type, canonicalName) != null)
            return false;

        var directory = Path.GetDirectoryName(doc.Path);
        if (directory == null)
            return false;

        var newPath = Path.Combine(directory, canonicalName + doc.Def.Extensions[0]);
        if (File.Exists(newPath))
            return false;

        // Rename companion files (e.g., .png alongside .sprite)
        foreach (var companionPath in GetCompanionFiles(doc).ToList())
        {
            var companionExt = Path.GetExtension(companionPath);
            var newCompanionPath = Path.Combine(directory, canonicalName + companionExt);
            File.Move(companionPath, newCompanionPath);
        }

        var oldMetaPath = doc.Path + ".meta";
        var newMetaPath = newPath + ".meta";

        File.Move(doc.Path, newPath);

        if (File.Exists(oldMetaPath))
            File.Move(oldMetaPath, newMetaPath);

        doc.Path = Path.GetFullPath(newPath).ToLowerInvariant();
        doc.Name = canonicalName;
        doc.IncrementVersion();

        AssetManifest.IsModified = true;

        return true;
    }

    public static void Delete(Document doc)
    {
        Undo.RemoveDocument(doc);

        // Delete companion files
        foreach (var companionPath in GetCompanionFiles(doc).ToList())
            File.Delete(companionPath);

        if (File.Exists(doc.Path))
            File.Delete(doc.Path);

        var metaPath = doc.Path + ".meta";
        if (File.Exists(metaPath))
            File.Delete(metaPath);

        AssetManifest.IsModified = true;

        _documents.Remove(doc);
        doc.Dispose();
    }

    private static void InitDocuments()
    {
        foreach (var sourcePath in _sourcePaths)
        {
            if (!Directory.Exists(sourcePath))
                continue;

            foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(filePath);
                if (ext == ".meta") continue;

                var def = GetDef(ext);
                if (def == null) continue;

                var name = MakeCanonicalName(filePath);
                if (Find(def.Type, name) != null) continue;

                Create(filePath);
            }
        }
    }

    public static string GetTargetPath(Document doc)
    {
        var typeName = (Asset.GetDef(doc.Def.Type)?.Name ?? doc.Def.Name).ToLowerInvariant();
        var filename = Path.GetFileNameWithoutExtension(doc.Path);
        var safeName = MakeCanonicalName(filename);
        return Path.Combine(_outputPath, typeName, safeName);
    }

    public static string MakeCanonicalName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.ToLowerInvariant()
            .Replace('/', '_')
            .Replace('.', '_')
            .Replace(' ', '_')
            .Replace('-', '_');
    }

    public static Document Get(int index) => _documents[index];

    public static string? GetUniquePath(string sourcePath)
    {
        var parentPath = Path.GetDirectoryName(sourcePath) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        var def = GetDef(ext);
        if (def == null) return null;
        var canonicalBase = MakeCanonicalName(fileName);

        var startIndex = 2;
        var lastUnderscore = canonicalBase.LastIndexOf('_');
        if (lastUnderscore > 0 && int.TryParse(canonicalBase[(lastUnderscore + 1)..], out var existingNum))
        {
            canonicalBase = canonicalBase[..lastUnderscore];
            startIndex = existingNum + 1;
        }

        for (var i = startIndex; ; i++)
        {
            var candidate = Path.Combine(parentPath, $"{canonicalBase}_{i}{ext}");

            if (File.Exists(candidate))
                continue;

            var canonicalName = MakeCanonicalName(candidate);
            if (Find(def.Type, canonicalName) != null)
                continue;

            return candidate;
        }
    }

    public static Document? Duplicate(Document source)
    {
        var newPath = GetUniquePath(source.Path);
        if (newPath == null) return null;
        var newName = Path.GetFileNameWithoutExtension(newPath);

        var directory = Path.GetDirectoryName(newPath);
        if (directory != null)
            Directory.CreateDirectory(directory);

        File.Copy(source.Path, newPath);

        // Copy companion files
        var newStem = Path.GetFileNameWithoutExtension(newPath);
        var newDir = Path.GetDirectoryName(newPath) ?? "";
        foreach (var companionPath in GetCompanionFiles(source))
        {
            var companionExt = Path.GetExtension(companionPath);
            File.Copy(companionPath, Path.Combine(newDir, newStem + companionExt));
        }

        var metaPath = source.Path + ".meta";
        if (File.Exists(metaPath))
            File.Copy(metaPath, newPath + ".meta");

        QueueExport(newPath);
        UpdateExports();

        var doc = Find(source.Def.Type, newName);
        if (doc == null) return null;

        // The export already loads, post-loads, and fires DocumentAdded via OnExported.
        // Only do it manually if the export didn't get to it (e.g. skipped by timestamp).
        if (!doc.PostLoaded)
        {
            doc.Load();
            doc.LoadMetadata();
            doc.PostLoad();
            doc.PostLoaded = true;
            DocumentAdded?.Invoke(doc);
        }

        doc.Position = source.Position;

        return doc;
    }

    // --- Export pipeline (moved from Importer.cs) ---

    public static void QueueExport(Document? doc, bool force = false)
    {
        if (doc == null) return;
        if (!doc.ShouldExport) return;
        if (doc.IsQueuedForExport) return;
        if (!File.Exists(doc.Path)) return;

        var targetPath = GetTargetPath(doc);
        var metaPath = doc.Path + ".meta";

        if (!force)
        {
            bool targetExists = File.Exists(targetPath);
            if (targetExists)
            {
                // Force export if the binary's version doesn't match the engine's expected version
                var assetDef = Asset.GetDef(doc.Def.Type);
                if (assetDef is { Version: > 0 } && Asset.ReadAssetVersion(targetPath) != assetDef.Version)
                    force = true;

                if (!force)
                {
                    var targetTime = File.GetLastWriteTimeUtc(targetPath);
                    var sourceTime = File.GetLastWriteTimeUtc(doc.Path);
                    var metaTime = File.Exists(metaPath) ? File.GetLastWriteTimeUtc(metaPath) : DateTime.MinValue;

                    if (sourceTime <= targetTime && metaTime <= targetTime)
                        return;
                }
            }
        }

        doc.IsQueuedForExport = true;
        _exportQueue.Enqueue(doc);
    }

    public static void QueueExport(string path)
    {
        var ext = Path.GetExtension(path);
        var def = GetDef(ext);
        if (def == null)
            return;

        var name = MakeCanonicalName(path);
        QueueExport(Find(def.Type, name) ?? Create(path));
    }

    private static bool IsFileReady(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void Export(Document doc)
    {
        try
        {
            if (!File.Exists(doc.Path))
                return;

            if (_watching && !IsFileReady(doc.Path))
            {
                _exportDeferred.Enqueue(doc);
                return;
            }

            var metaPath = doc.Path + ".meta";
            var meta = PropertySet.LoadFile(metaPath) ?? new PropertySet();
            var targetDir = GetTargetPath(doc);

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir) ?? "");

            doc.Export(targetDir, meta);

            Log.Info($"Exported {(Asset.GetDef(doc.Def.Type)?.Name ?? doc.Def.Type.ToString()).ToLowerInvariant()}/{doc.Name}");
            OnExported?.Invoke(doc);
            OnDocumentExported(doc);
            doc.SilentExport = false;
            AssetManifest.IsModified = true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to export '{doc.Name}': {ex.Message}");
        }
    }

    private static void StartWatching(string path)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Renamed += OnFileRenamed;

        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e) =>
        HandleFileChange(e.FullPath);

    private static void OnFileRenamed(object sender, RenamedEventArgs e) =>
        HandleFileChange(e.FullPath);

    private static void HandleFileChange(string path) =>
        _watcherQueue.Enqueue(Path.GetExtension(path) == ".meta" ? path[..^5] : path);

    public static async Task GenerateSpritesAsync(CancellationToken ct = default)
    {
        var sprites = _documents
            .OfType<SpriteDocument>()
            .Where(s => s.HasGeneration && !s.Generation.HasImageData)
            .ToList();

        if (sprites.Count == 0)
            return;

        Log.Info($"Generating images for {sprites.Count} sprite(s)...");

        foreach (var sprite in sprites)
        {
            if (ct.IsCancellationRequested)
                break;

            Log.Info($"Generating '{sprite.Name}'...");

            try
            {
                var request = sprite.BuildGenerationRequest();
                var status = await GenerationClient.GenerateAsync(request, cancellationToken: ct);

                if (status.State == GenerationState.Completed)
                {
                    sprite.ApplyGenerationResult(status, createTexture: false);
                    sprite.Save();
                    QueueExport(sprite, force: true);
                }
                else
                {
                    Log.Error($"Generation failed for '{sprite.Name}': {status.Error}");

                    if (status.Error != null && (status.Error.Contains("HttpRequestException") ||
                        status.Error.Contains("Connection refused") ||
                        status.Error.Contains("No connection") ||
                        status.Error.Contains("actively refused")))
                    {
                        var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";
                        Log.Error($"Generation server not available at {server}. Skipping remaining sprite generations.");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Generation error for '{sprite.Name}': {ex.Message}");

                if (ex is HttpRequestException)
                {
                    var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";
                    Log.Error($"Generation server not available at {server}. Skipping remaining sprite generations.");
                    break;
                }
            }
        }

        UpdateExports();
    }

    public static void QueueGenerations()
    {
        var sprites = _documents
            .OfType<SpriteDocument>()
            .Where(s => s.HasGeneration && !s.Generation.HasImageData)
            .ToList();

        if (sprites.Count == 0)
            return;

        foreach (var sprite in sprites)
            sprite.GenerateAsync();
    }

    public static void UpdateExports()
    {
        while (_watcherQueue.TryDequeue(out var path))
            QueueExport(path);

        while (_exportDeferred.Count > 0)
            _exportQueue.Enqueue(_exportDeferred.Dequeue());

        while (_exportQueue.Count > 0)
        {
            var doc = _exportQueue.Dequeue();
            doc.IsQueuedForExport = false;
            if (doc.IsDisposed) continue;
            Export(doc);
        }
    }
}
