//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class Outliner
{
    private static partial class ElementId
    {
        public static partial WidgetId FolderRow { get; }
        public static partial WidgetId FileRow { get; }
    }

    private struct FolderState
    {
        public byte Expanded;
    }

    private static int _folderIndex;
    private static int _fileIndex;

    // Cached tree, rebuilt when document count changes
    private static int _cachedDocCount;
    private static FolderNode? _cachedRoot;

    private class FolderNode
    {
        public string Name = "";
        public readonly List<FolderNode> Folders = [];
        public readonly List<Document> Documents = [];
    }

    private static void ProjectViewUI()
    {
        _folderIndex = 0;
        _fileIndex = 0;

        var root = GetOrBuildTree();
        if (root == null) return;

        // Render each source root's children directly (skip the root node)
        foreach (var folder in root.Folders)
            FolderUI(folder, 0);

        foreach (var doc in root.Documents)
            FileUI(doc, 0);
    }

    private static FolderNode GetOrBuildTree()
    {
        var count = DocumentManager.Documents.Count;
        if (_cachedRoot != null && _cachedDocCount == count)
            return _cachedRoot;

        _cachedDocCount = count;
        _cachedRoot = BuildTree();
        return _cachedRoot;
    }

    private static FolderNode BuildTree()
    {
        var root = new FolderNode { Name = "" };

        foreach (var doc in DocumentManager.Documents)
        {
            // Find which source path this document belongs to
            var relativePath = GetRelativePath(doc.Path);
            if (relativePath == null) continue;

            // Split into directory parts and filename
            var parts = relativePath.Replace('\\', '/').Split('/');
            var current = root;

            // Walk directory parts, creating folder nodes as needed
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var part = parts[i];
                var child = current.Folders.Find(f =>
                    string.Equals(f.Name, part, StringComparison.OrdinalIgnoreCase));
                if (child == null)
                {
                    child = new FolderNode { Name = part };
                    current.Folders.Add(child);
                }
                current = child;
            }

            current.Documents.Add(doc);
        }

        // Sort folders and documents
        SortTree(root);
        return root;
    }

    private static void SortTree(FolderNode node)
    {
        node.Folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        node.Documents.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var child in node.Folders)
            SortTree(child);
    }

    private static string? GetRelativePath(string docPath)
    {
        foreach (var sourcePath in DocumentManager.SourcePaths)
        {
            var normalized = sourcePath.Replace('\\', '/').ToLowerInvariant().TrimEnd('/') + '/';
            var normalizedDoc = docPath.Replace('\\', '/').ToLowerInvariant();
            if (normalizedDoc.StartsWith(normalized))
                return docPath[normalized.Length..];
        }
        return null;
    }

    private static void FolderUI(FolderNode folder, int depth)
    {
        var folderId = ElementId.FolderRow + _folderIndex++;

        ElementTree.BeginTree();
        ref var state = ref ElementTree.BeginWidget<FolderState>(folderId);
        var flags = ElementTree.GetWidgetFlags();
        var expanded = state.Expanded != 0;

        if (flags.HasFlag(WidgetFlags.Pressed))
            state.Expanded = (byte)(expanded ? 0 : 1);

        var hovered = flags.HasFlag(WidgetFlags.Hovered);
        var bg = hovered ? EditorStyle.Palette.Active : Color.Transparent;

        ElementTree.BeginSize(Size.Default, EditorStyle.Control.Height);
        ElementTree.BeginFill(bg, radius: EditorStyle.Control.BorderRadius);
        ElementTree.BeginPadding(EdgeInsets.Left(depth * 16 + 4));

        ElementTree.BeginRow(EditorStyle.Control.Spacing);

        var chevron = expanded
            ? EditorAssets.Sprites.IconFoldoutClosed
            : EditorAssets.Sprites.IconFoldoutOpen;
        ElementTree.Image(chevron, EditorStyle.Icon.Size, ImageStretch.Uniform,
            EditorStyle.Palette.SecondaryText, 1.0f, Align.Center);

        ElementTree.Text(folder.Name, UI.DefaultFont, EditorStyle.Control.TextSize,
            EditorStyle.Palette.Content, new Align2(Align.Min, Align.Center));

        ElementTree.EndTree();

        if (expanded)
        {
            foreach (var child in folder.Folders)
                FolderUI(child, depth + 1);
            foreach (var doc in folder.Documents)
                FileUI(doc, depth + 1);
        }
    }

    private static void FileUI(Document doc, int depth)
    {
        var rowId = ElementId.FileRow + _fileIndex++;

        ElementTree.BeginTree();
        ref var _ = ref ElementTree.BeginWidget<byte>(rowId);
        var flags = ElementTree.GetWidgetFlags();
        var hovered = flags.HasFlag(WidgetFlags.Hovered);
        var pressed = flags.HasFlag(WidgetFlags.Pressed);

        var bg = doc.IsSelected || hovered
            ? EditorStyle.Palette.Active
            : Color.Transparent;

        ElementTree.BeginSize(Size.Default, EditorStyle.Control.Height);
        ElementTree.BeginFill(bg, radius: EditorStyle.Control.BorderRadius);
        ElementTree.BeginPadding(EdgeInsets.Left(depth * 16 + 4 + EditorStyle.Icon.Size + EditorStyle.Control.Spacing));

        ElementTree.BeginRow(EditorStyle.Control.Spacing);

        // Asset type icon
        var icon = doc.Def.Icon?.Invoke();
        if (icon != null)
            ElementTree.Image(icon, EditorStyle.Control.IconSize, ImageStretch.Uniform,
                EditorStyle.Palette.SecondaryText, 1.0f, new Align2(Align.Center, Align.Center));

        // File name (without extension)
        ElementTree.Text(Path.GetFileNameWithoutExtension(doc.Path), UI.DefaultFont,
            EditorStyle.Control.TextSize, EditorStyle.Palette.Content, new Align2(Align.Min, Align.Center));

        ElementTree.EndTree();

        if (pressed)
        {
            Workspace.ClearSelection();
            Workspace.SetSelected(doc, true);
            Workspace.FrameSelected();
        }
    }

    public static void InvalidateProjectView()
    {
        _cachedRoot = null;
    }
}
