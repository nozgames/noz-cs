//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Linq;

namespace NoZ.Editor;

internal partial class VfxEditor
{
    private string? _renameText;
    private VfxSelectionType _renameType;
    private int _renameIndex = -1;

    private bool IsRenaming => _renameIndex >= 0;

    public override void OutlinerUI()
    {
        if (IsRenaming)
        {
            if (Input.WasButtonPressed(InputCode.KeyEnter, InputScope.All))
            {
                Input.ConsumeButton(InputCode.KeyEnter);
                CommitRename();
            }
            else if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
            {
                Input.ConsumeButton(InputCode.KeyEscape);
                CancelRename();
            }
        }

        // F2 to begin rename on selected item (single selection only)
        if (!IsRenaming && !Document.HasMultiSelection &&
            Document.SingleSelectedType is VfxSelectionType.Emitter or VfxSelectionType.Particle &&
            Input.WasButtonPressed(InputCode.KeyF2, InputScope.All))
        {
            var name = Document.SingleSelectedType == VfxSelectionType.Emitter
                ? Document.Emitters[Document.SingleSelectedIndex].Name
                : Document.Particles[Document.SingleSelectedIndex].Name;
            BeginRename(Document.SingleSelectedType, Document.SingleSelectedIndex, name);
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
        var isSelected = Document.VfxRootSelected;
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
            UI.ClearHot();
            Document.SelectVfxRoot();
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

        using (Outliner.BeginSection("EMITTERS", content: AddButton))
        {
            if (Outliner.IsSectionCollapsed) return;

            for (var i = 0; i < Document.Emitters.Count; i++)
            {
                var isSelected = Document.SelectedEmitters.Contains(i);
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

        using (Outliner.BeginSection("PARTICLES", content: AddButton))
        {
            if (Outliner.IsSectionCollapsed) return;

            for (var i = 0; i < Document.Particles.Count; i++)
            {
                var isSelected = Document.SelectedParticles.Contains(i);
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
            ElementTree.BeginFlex();
            if (isRenaming)
            {
                ElementTree.BeginMargin(EdgeInsets.TopLeft(2, -2));
                _renameText = UI.TextInput(ElementId.RenameInput, _renameText ?? name, EditorStyle.SpriteEditor.OutlinerRename);
                ElementTree.EndMargin();

                if (UI.HotExit())
                    CommitRename();
            }
            else
            {
                UI.Text(name, EditorStyle.Text.Primary);
            }
            ElementTree.EndFlex();
        }

        // Click to select (Ctrl+click to toggle multi-select)
        if (UI.WasPressed(rowId) && !isRenaming)
        {
            if (IsRenaming) CommitRename();
            UI.ClearHot();

            if (Input.IsCtrlDown(InputScope.All))
            {
                if (type == VfxSelectionType.Emitter)
                    Document.ToggleEmitter(index);
                else
                    Document.ToggleParticle(index);
            }
            else
            {
                if (type == VfxSelectionType.Emitter)
                    Document.SelectEmitter(index);
                else
                    Document.SelectParticle(index);
            }
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
        if (!Document.HasSelection || Document.VfxRootSelected) return;

        Undo.Record(Document);

        // Delete in reverse index order to avoid index shifting issues
        var emitterIndices = Document.SelectedEmitters.OrderDescending().ToList();
        var particleIndices = Document.SelectedParticles.OrderDescending().ToList();

        foreach (var i in emitterIndices)
            Document.RemoveEmitter(i);
        foreach (var i in particleIndices)
            Document.RemoveParticle(i);
    }

    private void DuplicateSelected()
    {
        if (IsRenaming) return;
        if (!Document.HasSelection || Document.VfxRootSelected) return;

        Undo.Record(Document);

        // Collect sources before modifying lists
        var emitterSources = Document.SelectedEmitters.OrderBy(i => i)
            .Where(i => i >= 0 && i < Document.Emitters.Count)
            .Select(i => Document.Emitters[i]).ToList();
        var particleSources = Document.SelectedParticles.OrderBy(i => i)
            .Where(i => i >= 0 && i < Document.Particles.Count)
            .Select(i => Document.Particles[i]).ToList();

        Document.ClearSelection();

        // Duplicate particles first, build name mapping for reference fixup
        var particleNameMap = new Dictionary<string, string>();
        foreach (var src in particleSources)
        {
            var newName = MakeUniqueName(src.Name, Document.Particles.Select(p => p.Name));
            particleNameMap[src.Name] = newName;
            Document.Particles.Add(new VfxDocParticle
            {
                Name = newName,
                Def = src.Def,
                SpriteRef = src.SpriteRef,
            });
            Document.ToggleParticle(Document.Particles.Count - 1);
        }

        // Duplicate emitters, remapping particle refs if the particle was also duplicated
        var copiedParticleNames = new HashSet<string>(particleSources.Select(p => p.Name));
        foreach (var src in emitterSources)
        {
            var particleRef = src.ParticleRef;
            if (copiedParticleNames.Contains(particleRef))
                particleRef = particleNameMap.GetValueOrDefault(particleRef, particleRef);

            Document.Emitters.Add(new VfxDocEmitter
            {
                Name = MakeUniqueName(src.Name, Document.Emitters.Select(e => e.Name)),
                Def = src.Def,
                ParticleRef = particleRef,
            });
            Document.ToggleEmitter(Document.Emitters.Count - 1);
        }

        Document.ApplyChanges();
    }

    private void CopySelected()
    {
        if (IsRenaming) return;
        if (!Document.HasSelection || Document.VfxRootSelected) return;

        var emitters = Document.SelectedEmitters.OrderBy(i => i)
            .Where(i => i >= 0 && i < Document.Emitters.Count)
            .Select(i => Document.Emitters[i]).ToList();
        var particles = Document.SelectedParticles.OrderBy(i => i)
            .Where(i => i >= 0 && i < Document.Particles.Count)
            .Select(i => Document.Particles[i]).ToList();

        if (emitters.Count == 0 && particles.Count == 0) return;
        Clipboard.Copy(new VfxClipboardData(emitters, particles));
    }

    private void PasteFromClipboard()
    {
        var data = Clipboard.Get<VfxClipboardData>();
        if (data == null) return;

        Undo.Record(Document);
        Document.ClearSelection();

        // Paste particles first, building name mapping for reference fixup
        var particleNameMap = new Dictionary<string, string>();
        var copiedParticleNames = new HashSet<string>(data.Particles.Select(p => p.Name));
        foreach (var pData in data.Particles)
        {
            if (Document.Particles.Count >= 32) break; // MaxParticles
            var newName = MakeUniqueName(pData.Name, Document.Particles.Select(p => p.Name));
            particleNameMap[pData.Name] = newName;
            Document.Particles.Add(new VfxDocParticle
            {
                Name = newName,
                Def = pData.Def,
                SpriteRef = pData.SpriteRef,
            });
            Document.ToggleParticle(Document.Particles.Count - 1);
        }

        // Paste emitters, remapping ParticleRef
        foreach (var eData in data.Emitters)
        {
            if (Document.Emitters.Count >= 32) break; // MaxEmitters
            var particleRef = eData.ParticleRef;
            if (copiedParticleNames.Contains(particleRef))
                particleRef = particleNameMap.GetValueOrDefault(particleRef, "");
            else if (Document.FindParticle(particleRef) == null)
                particleRef = "";

            Document.Emitters.Add(new VfxDocEmitter
            {
                Name = MakeUniqueName(eData.Name, Document.Emitters.Select(e => e.Name)),
                Def = eData.Def,
                ParticleRef = particleRef,
            });
            Document.ToggleEmitter(Document.Emitters.Count - 1);
        }

        Document.ApplyChanges();
    }

    private void CutSelected()
    {
        CopySelected();
        DeleteSelected();
    }

    private static string MakeUniqueName(string baseName, IEnumerable<string> existingNames)
    {
        // Strip trailing _N suffix
        var lastUnderscore = baseName.LastIndexOf('_');
        if (lastUnderscore > 0 && int.TryParse(baseName[(lastUnderscore + 1)..], out _))
            baseName = baseName[..lastUnderscore];

        var names = new HashSet<string>(existingNames);
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }
}
