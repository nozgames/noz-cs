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
    private static readonly ConcurrentQueue<string> _watcherQueue = [];
    private static readonly ConcurrentQueue<string> _reloadQueue = [];
    private static bool _watching;

    public static IReadOnlyList<Document> Documents => _documents;
    public static int Count => _documents.Count;
    public static Document GetAt(int index) => _documents[index];
    public static IReadOnlyList<string> SourcePaths => _sourcePaths;
    public static string OutputPath => _outputPath;

    public static IEnumerable<Document> SelectedDocuments =>
        _documents.Where(d => d.IsSelected);

    public delegate void DocumentAddedDelegate(Document doc);

    public static event DocumentAddedDelegate? DocumentAdded;
    public static event DocumentAddedDelegate? DocumentRemoved;
    public static event Action<Document>? OnExported;

    public static void NotifyDocumentAdded(Document doc) => DocumentAdded?.Invoke(doc);

    private static readonly Dictionary<AssetType, DocumentDef> _defsByType = new();
    private static readonly Dictionary<string, List<DocumentDef>> _defsByExtension = new();

    public static void Init(string[] sourcePaths, string outputPath)
    {
        _sourcePaths.Clear();
        _sourcePaths.AddRange(sourcePaths);
        _outputPath = outputPath;

        Log.Info($"DocumentOutputPath: {outputPath}");
        foreach (var path in sourcePaths)
            Log.Info($"DocumentSourcePath: {path}");

        EditorApplication.Store.CreateDirectory(outputPath);

        InitDocuments();
    }

    public static void InitExports(bool clean = false)
    {
        if (clean)
            Log.Info("Clean build requested, exporting all assets...");

        foreach (var doc in _documents)
            QueueExport(doc, clean);

        UpdateExports();

        var store = EditorApplication.Store;
        store.FileChanged += HandleFileChange;
        foreach (var sourcePath in _sourcePaths)
            if (store.DirectoryExists(sourcePath))
                store.StartWatching(sourcePath);

        _watching = true;
    }

    public static void Shutdown()
    {
        var store = EditorApplication.Store;
        store.FileChanged -= HandleFileChange;
        store.StopWatching();

        _initialized = false;
        _documents.Clear();
        _sourcePaths.Clear();
    }

    public static void RegisterDef(DocumentDef def)
    {
        _defsByType[def.Type] = def;
        foreach (var ext in def.Extensions)
        {
            if (!_defsByExtension.TryGetValue(ext, out var list))
                _defsByExtension[ext] = list = [];
            list.Add(def);
        }
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
        return _defsByExtension.TryGetValue(ext, out var list) && list.Count > 0 ? list[0] : null;
    }

    public static IReadOnlyList<DocumentDef>? GetDefs(string ext)
    {
        ext = ext.ToLowerInvariant();
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        return _defsByExtension.TryGetValue(ext, out var list) ? list : null;
    }

    private static bool IsAuxiliaryFile(string path)
    {
        var filename = Path.GetFileName(path);
        foreach (var def in _defsByType.Values)
        {
            if (def.AuxiliaryExtensions == null) continue;
            foreach (var auxExt in def.AuxiliaryExtensions)
            {
                if (filename.EndsWith(auxExt, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static string? GetAuxiliaryParentPath(string path)
    {
        var dir = GetDirectory(path);
        var filename = Path.GetFileName(path);
        foreach (var def in _defsByType.Values)
        {
            if (def.AuxiliaryExtensions == null) continue;
            foreach (var auxExt in def.AuxiliaryExtensions)
            {
                if (!filename.EndsWith(auxExt, StringComparison.OrdinalIgnoreCase)) continue;
                var stem = filename[..^auxExt.Length];
                foreach (var primaryExt in def.Extensions)
                {
                    var candidate = CombinePath(dir, stem + primaryExt);
                    if (EditorApplication.Store.FileExists(candidate))
                        return candidate;
                }
            }
        }
        return null;
    }

    public static DocumentDef? ResolveDef(string path)
    {
        var ext = Path.GetExtension(path);
        var defs = GetDefs(ext);
        if (defs == null || defs.Count == 0)
            return null;
        if (defs.Count == 1)
            return defs[0];

        var metaPath = path + ".meta";
        var store = EditorApplication.Store;
        if (store.FileExists(metaPath))
        {
            var meta = PropertySetExtensions.LoadFile(store, metaPath);
            var docType = meta?.GetString("editor", "document_type", "");
            if (!string.IsNullOrEmpty(docType))
            {
                foreach (var def in defs)
                {
                    if (string.Equals(def.Name, docType, StringComparison.OrdinalIgnoreCase))
                        return def;
                }
            }
        }

        return defs[0];
    }

    public static Document? Create(string path)
    {
        var def = ResolveDef(path);
        if (def == null)
            return null;

        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();
        var doc = def.Factory(normalizedPath);
        doc.Def = def;
        doc.Path = normalizedPath;
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

    public static Document? New(AssetType assetType, string extension, string? name, System.Numerics.Vector2? position = null, Action<StreamWriter>? writeContent = null)
    {
        if (_sourcePaths.Count == 0)
            return null;

        var typeName = (Asset.GetDef(assetType)?.Name ?? assetType.ToString()).ToLowerInvariant();
        name = name ?? MakeCanonicalName($"new_{typeName}");
        name = GenerateUniqueName(assetType, name);

        var canonicalName = MakeCanonicalName(name);
        if (Find(assetType, canonicalName) != null)
            return null;
        var path = CombinePath(CombinePath(_sourcePaths[0], typeName), canonicalName + extension);
        var store = EditorApplication.Store;
        if (store.FileExists(path))
            return null;

        var directory = GetDirectory(path);
        if (!string.IsNullOrEmpty(directory))
            store.CreateDirectory(directory);

        // Write file content before creating document
        if (writeContent != null)
        {
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms);
            writeContent(writer);
            writer.Flush();
            store.WriteAllBytes(path, ms.ToArray());
        }
        else
        {
            store.WriteAllBytes(path, []);
        }

        var doc = Create(path);
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

    public static T? Find<T>(string name) where T : Document
        => Find(DocumentDef<T>.Def.Type, name) as T;

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
        var store = EditorApplication.Store;
        var directory = GetDirectory(doc.Path);
        var stem = Path.GetFileNameWithoutExtension(doc.Path);
        foreach (var ext in doc.Def.Extensions)
        {
            var path = CombinePath(directory, stem + ext);
            if (store.FileExists(path) && !string.Equals(path, doc.Path, StringComparison.OrdinalIgnoreCase))
                yield return path;
        }

        if (doc.Def.AuxiliaryExtensions != null)
        {
            foreach (var auxExt in doc.Def.AuxiliaryExtensions)
            {
                var path = CombinePath(directory, stem + auxExt);
                if (store.FileExists(path))
                    yield return path;
            }
        }
    }

    public static bool Rename(Document doc, string name)
    {
        var canonicalName = MakeCanonicalName(name);
        if (Find(doc.Def.Type, canonicalName) != null)
            return false;

        var directory = GetDirectory(doc.Path);
        if (string.IsNullOrEmpty(directory))
            return false;

        var newPath = CombinePath(directory, canonicalName + doc.Def.Extensions[0]);
        var store = EditorApplication.Store;
        if (store.FileExists(newPath))
            return false;

        // Rename companion files (e.g., .png alongside .sprite)
        foreach (var companionPath in GetCompanionFiles(doc).ToList())
        {
            var companionExt = Path.GetExtension(companionPath);
            var newCompanionPath = CombinePath(directory, canonicalName + companionExt);
            store.MoveFile(companionPath, newCompanionPath);
        }

        var oldMetaPath = doc.Path + ".meta";
        var newMetaPath = newPath + ".meta";

        store.MoveFile(doc.Path, newPath);

        if (store.FileExists(oldMetaPath))
            store.MoveFile(oldMetaPath, newMetaPath);

        var oldName = doc.Name;

        doc.Path = newPath.Replace('\\', '/').ToLowerInvariant();
        doc.Name = canonicalName;
        doc.IncrementVersion();

        foreach (var other in _documents)
        {
            if (other == doc || !other.Loaded) continue;
            other.OnRenamed(doc, oldName, canonicalName);
        }

        AssetManifest.IsModified = true;

        return true;
    }

    public static Document? ChangeType(Document doc, DocumentDef newDef)
    {
        if (doc.Def == newDef)
            return doc;

        var path = doc.Path;
        var position = doc.Position;
        var collectionId = doc.CollectionId;
        var shouldExport = doc.ShouldExport;
        var wasSelected = doc.IsSelected;

        // Update the meta file with the new document type
        var metaPath = path + ".meta";
        var store = EditorApplication.Store;
        var meta = PropertySetExtensions.LoadFile(store, metaPath) ?? new PropertySet();
        meta.SetString("editor", "document_type", newDef.Name);
        meta.Save(metaPath, store);

        // Remove old document
        Workspace.ClearSelection();
        _documents.Remove(doc);
        doc.Dispose();

        // Create new document from same path (ResolveDef will read the updated meta)
        var newDoc = Create(path);
        if (newDoc == null)
            return null;

        newDoc.Loaded = true;
        newDoc.Load();
        newDoc.LoadMetadata();
        newDoc.Position = position;
        newDoc.CollectionId = collectionId;
        newDoc.ShouldExport = shouldExport;
        newDoc.PostLoad();
        newDoc.PostLoaded = true;

        if (wasSelected)
            Workspace.SetSelected(newDoc, true);

        AssetManifest.IsModified = true;

        return newDoc;
    }

    public static void Remove(Document doc)
    {
        _documents.Remove(doc);
        doc.Dispose();
    }

    public static void Delete(Document doc)
    {
        Undo.RemoveDocument(doc);

        var store = EditorApplication.Store;

        // Delete companion files
        foreach (var companionPath in GetCompanionFiles(doc).ToList())
            store.DeleteFile(companionPath);

        if (store.FileExists(doc.Path))
            store.DeleteFile(doc.Path);

        var metaPath = doc.Path + ".meta";
        if (store.FileExists(metaPath))
            store.DeleteFile(metaPath);

        AssetManifest.IsModified = true;

        _documents.Remove(doc);
        DocumentRemoved?.Invoke(doc);
        doc.Dispose();
    }

    private static void InitDocuments()
    {
        var store = EditorApplication.Store;
        foreach (var sourcePath in _sourcePaths)
        {
            if (!store.DirectoryExists(sourcePath))
                continue;

            foreach (var filePath in store.EnumerateFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(filePath);
                if (ext == ".meta") continue;
                if (IsAuxiliaryFile(filePath)) continue;

                var def = ResolveDef(filePath);
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
        return CombinePath(CombinePath(_outputPath, typeName), safeName);
    }

    private static string CombinePath(string a, string b) =>
        Path.Combine(a, b).Replace('\\', '/');

    private static string GetDirectory(string path) =>
        (Path.GetDirectoryName(path) ?? "").Replace('\\', '/');

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
        var parentPath = GetDirectory(sourcePath);
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
            var candidate = CombinePath(parentPath, $"{canonicalBase}_{i}{ext}");

            if (EditorApplication.Store.FileExists(candidate))
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

        var store = EditorApplication.Store;
        var directory = GetDirectory(newPath);
        if (!string.IsNullOrEmpty(directory))
            store.CreateDirectory(directory);

        store.CopyFile(source.Path, newPath);

        // Copy companion files
        var newStem = Path.GetFileNameWithoutExtension(newPath);
        var newDir = GetDirectory(newPath);
        foreach (var companionPath in GetCompanionFiles(source))
        {
            var companionExt = Path.GetExtension(companionPath);
            store.CopyFile(companionPath, CombinePath(newDir, newStem + companionExt));
        }

        var metaPath = source.Path + ".meta";
        if (store.FileExists(metaPath))
            store.CopyFile(metaPath, newPath + ".meta");

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

        // Re-export now that the document is loaded and atlas-assigned
        if (doc.ShouldExport)
        {
            QueueExport(doc, force: true);
            UpdateExports();
        }

        // Ensure the atlas is rebuilt with the duplicate's rasterized pixels
        if (doc is SpriteDocument { Atlas: not null } sprite)
            AtlasManager.UpdateSource(sprite);

        doc.Position = source.Position;

        return doc;
    }

    // --- Export pipeline (moved from Importer.cs) ---

    public static void QueueExport(Document? doc, bool force = false)
    {
        if (doc == null) return;
        if (!doc.ShouldExport) return;
        if (doc.IsQueuedForExport) return;

        var store = EditorApplication.Store;
        if (!store.FileExists(doc.Path)) return;

        var targetPath = GetTargetPath(doc);
        var metaPath = doc.Path + ".meta";

        if (!force)
        {
            bool targetExists = store.FileExists(targetPath);
            if (targetExists)
            {
                // Force export if the binary's version doesn't match the engine's expected version
                var assetDef = Asset.GetDef(doc.Def.Type);
                if (assetDef is { Version: > 0 } && ReadAssetVersion(store, targetPath) != assetDef.Version)
                    force = true;

                if (!force)
                {
                    var targetTime = store.GetLastWriteTimeUtc(targetPath);
                    var sourceTime = store.GetLastWriteTimeUtc(doc.Path);
                    var metaTime = store.FileExists(metaPath) ? store.GetLastWriteTimeUtc(metaPath) : DateTime.MinValue;

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
        var def = ResolveDef(path);
        if (def == null)
            return;

        var name = MakeCanonicalName(path);
        QueueExport(Find(def.Type, name) ?? Create(path));
    }

    private static ushort ReadAssetVersion(IEditorStore store, string path)
    {
        try
        {
            using var stream = store.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 12) return 0;
            reader.ReadUInt32(); // signature
            reader.ReadUInt32(); // type FourCC
            return reader.ReadUInt16();
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsFileReady(string path)
    {
        try
        {
            using var stream = EditorApplication.Store.OpenRead(path);
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
            var store = EditorApplication.Store;
            if (!store.FileExists(doc.Path))
                return;

            if (_watching && !IsFileReady(doc.Path))
            {
                _exportDeferred.Enqueue(doc);
                return;
            }

            var metaPath = doc.Path + ".meta";
            var meta = PropertySetExtensions.LoadFile(store, metaPath) ?? new PropertySet();
            var targetDir = GetTargetPath(doc);

            store.CreateDirectory(GetDirectory(targetDir));

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

    private static void HandleFileChange(string path)
    {
        if (Path.GetExtension(path) == ".meta")
        {
            _watcherQueue.Enqueue(path[..^5]);
            return;
        }

        if (IsAuxiliaryFile(path))
        {
            var parentPath = GetAuxiliaryParentPath(path);
            if (parentPath != null)
                _watcherQueue.Enqueue(parentPath);
            return;
        }

        _watcherQueue.Enqueue(path);
        _reloadQueue.Enqueue(path);
    }

    public static async Task GenerateSpritesAsync(CancellationToken ct = default)
    {
        var sprites = _documents
            .OfType<GeneratedSpriteDocument>()
            .Where(s => !s.Generation.HasImageData)
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
            .OfType<GeneratedSpriteDocument>()
            .Where(s => !s.Generation.HasImageData)
            .ToList();

        if (sprites.Count == 0)
            return;

        foreach (var sprite in sprites)
            sprite.GenerateAsync();
    }

    public static void UpdateExports()
    {
        while (_reloadQueue.TryDequeue(out var path))
        {
            var def = ResolveDef(path);
            if (def != null)
            {
                var name = MakeCanonicalName(path);
                var doc = Find(def.Type, name);
                if (doc is { Loaded: true } && !doc.SilentExport)
                    doc.Reload();
            }
        }

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
