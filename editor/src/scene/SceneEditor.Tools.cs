//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class SceneEditor
{
    private void DeleteSelected()
    {
        if (_selectedNodes.Count == 0) return;

        Undo.Record(Document);

        foreach (var node in _selectedNodes.ToList())
            node.RemoveFromParent();

        _selectedNodes.Clear();
        UpdateSelectionBounds();
    }

    private void DuplicateSelected()
    {
        if (_selectedNodes.Count == 0) return;

        Undo.Record(Document);

        var clones = new List<SceneNode>();
        foreach (var node in EffectiveSelection())
        {
            var parent = node.Parent;
            if (parent == null) continue;

            var clone = node.Clone();
            // Reset selection state on the clone
            clone.ForEach(n => n.IsSelected = false);

            var idx = parent.Children.IndexOf(node);
            parent.Insert(idx + 1, clone);
            clones.Add(clone);
        }

        Document.Root.ClearSelection();
        foreach (var clone in clones)
            clone.IsSelected = true;
        RebuildSelection();
    }

    private void CopySelected()
    {
        if (_selectedNodes.Count == 0) return;
        var data = new SceneClipboardData([.. EffectiveSelection()]);
        if (data.Roots.Length > 0)
            Clipboard.Copy(data);
    }

    private void PasteSelected()
    {
        var data = Clipboard.Get<SceneClipboardData>();
        if (data == null) return;

        Undo.Record(Document);

        SceneGroup parent = Document.Root;
        if (_selectedNodes.Count == 1 && _selectedNodes[0] is SceneGroup g)
            parent = g;

        Document.Root.ClearSelection();

        var pasted = data.PasteAsNodes();
        foreach (var node in pasted)
        {
            parent.Add(node);
            node.IsSelected = true;
        }
        parent.Expanded = true;
        RebuildSelection();
    }

    private void CutSelected()
    {
        if (_selectedNodes.Count == 0) return;
        CopySelected();
        DeleteSelected();
    }

    private void GroupSelected()
    {
        if (_selectedNodes.Count == 0) return;

        Undo.Record(Document);

        var members = EffectiveSelection().ToList();
        if (members.Count == 0) return;

        var first = members[0];
        var parent = first.Parent ?? Document.Root;
        var insertIdx = parent.Children.IndexOf(first);
        if (insertIdx < 0) insertIdx = 0;

        var group = new SceneGroup { Name = "Group" };
        parent.Insert(insertIdx, group);

        foreach (var n in members)
            n.RemoveFromParent();
        foreach (var n in members)
            group.Add(n);

        Document.Root.ClearSelection();
        group.IsSelected = true;
        group.Expanded = true;
        RebuildSelection();
    }
}
