//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal partial class VfxEditor
{
    private string? _renameText;
    private VfxSelectionType _renameType;
    private int _renameIndex = -1;

    private bool IsRenaming => _renameIndex >= 0;

    private void OutlinerUI()
    {
        // Handle rename cancel via Escape
        if (IsRenaming && Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
            CancelRename();

        // Handle rename commit via Enter
        if (IsRenaming && Input.WasButtonPressed(InputCode.KeyEnter, InputScope.All))
            CommitRename();

        // F2 to begin rename on selected item
        if (!IsRenaming && Document.SelectedType != VfxSelectionType.None &&
            Input.WasButtonPressed(InputCode.KeyF2, InputScope.All))
        {
            var name = Document.SelectedType == VfxSelectionType.Emitter
                ? Document.Emitters[Document.SelectedIndex].Name
                : Document.Particles[Document.SelectedIndex].Name;
            BeginRename(Document.SelectedType, Document.SelectedIndex, name);
        }

        using (UI.BeginColumn(ElementId.OutlinerPanel, EditorStyle.Inspector.Root))
        {
            VfxRootUI();
            EmitterListUI();
            ParticleListUI();
        }
    }

    private void VfxRootUI()
    {
        var isSelected = Document.SelectedType == VfxSelectionType.Vfx;
        var style = EditorStyle.Inspector.ListItem;
        if (isSelected)
            style = style with { Background = EditorStyle.Palette.Active };

        using (UI.BeginRow(ElementId.VfxRoot, style))
        {
            UI.Image(EditorAssets.Sprites.AssetIconVfx, EditorStyle.Control.IconSecondary);
            using (UI.BeginFlex())
                UI.Text(Document.Name, EditorStyle.Text.Primary);
        }

        if (UI.WasPressed(ElementId.VfxRoot))
        {
            if (IsRenaming) CommitRename();
            Document.SelectedType = VfxSelectionType.Vfx;
            Document.SelectedIndex = -1;
        }
    }

    private void EmitterListUI()
    {
        void AddButton()
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(ElementId.AddEmitterButton, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
            {
                Undo.Record(Document);
                Document.AddEmitter($"emitter{Document.Emitters.Count}");
            }
            ElementTree.EndAlign();
        }

        using (Inspector.BeginSection("EMITTERS", content: AddButton))
        {
            if (Inspector.IsSectionCollapsed) return;

            for (var i = 0; i < Document.Emitters.Count; i++)
            {
                var isSelected = Document.SelectedType == VfxSelectionType.Emitter && Document.SelectedIndex == i;
                var isRenaming = _renameType == VfxSelectionType.Emitter && _renameIndex == i;
                OutlinerRowUI(ElementId.EmitterRow + i, Document.Emitters[i].Name, isSelected, isRenaming, VfxSelectionType.Emitter, i);
            }
        }
    }

    private void ParticleListUI()
    {
        void AddButton()
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(ElementId.AddParticleButton, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
            {
                Undo.Record(Document);
                Document.AddParticle($"particle{Document.Particles.Count}");
            }
            ElementTree.EndAlign();
        }

        using (Inspector.BeginSection("PARTICLES", content: AddButton))
        {
            if (Inspector.IsSectionCollapsed) return;

            for (var i = 0; i < Document.Particles.Count; i++)
            {
                var isSelected = Document.SelectedType == VfxSelectionType.Particle && Document.SelectedIndex == i;
                var isRenaming = _renameType == VfxSelectionType.Particle && _renameIndex == i;
                OutlinerRowUI(ElementId.ParticleRow + i, Document.Particles[i].Name, isSelected, isRenaming, VfxSelectionType.Particle, i);
            }
        }
    }

    private void OutlinerRowUI(WidgetId rowId, string name, bool isSelected, bool isRenaming, VfxSelectionType type, int index)
    {
        var style = EditorStyle.Inspector.ListItem;
        if (isSelected)
            style = style with { Background = EditorStyle.Palette.Active };

        using (UI.BeginRow(rowId, style))
        {
            if (isRenaming)
            {
                var newName = UI.TextInput(ElementId.RenameInput, _renameText ?? name, EditorStyle.Inspector.TextBox);
                if (newName != _renameText)
                    _renameText = newName;
            }
            else
            {
                using (UI.BeginFlex())
                    UI.Text(name, EditorStyle.Text.Primary);
            }
        }

        // Click to select
        if (UI.WasPressed(rowId) && !isRenaming)
        {
            if (IsRenaming) CommitRename();

            Document.SelectedType = type;
            Document.SelectedIndex = index;
        }

    }

    private void BeginRename(VfxSelectionType type, int index, string currentName)
    {
        _renameType = type;
        _renameIndex = index;
        _renameText = currentName;
        UI.SetHot(ElementId.RenameInput);
    }

    private void CommitRename()
    {
        if (_renameText != null && _renameIndex >= 0 && !string.IsNullOrWhiteSpace(_renameText))
        {
            Undo.Record(Document);
            if (_renameType == VfxSelectionType.Emitter)
                Document.RenameEmitter(_renameIndex, _renameText);
            else
                Document.RenameParticle(_renameIndex, _renameText);
        }

        _renameText = null;
        _renameIndex = -1;
    }

    private void CancelRename()
    {
        _renameText = null;
        _renameIndex = -1;
    }

    private void DeleteSelected()
    {
        if (IsRenaming) return;
        if (Document.SelectedType == VfxSelectionType.Emitter)
        {
            Undo.Record(Document);
            Document.RemoveEmitter(Document.SelectedIndex);
        }
        else if (Document.SelectedType == VfxSelectionType.Particle)
        {
            Undo.Record(Document);
            Document.RemoveParticle(Document.SelectedIndex);
        }
    }
}
