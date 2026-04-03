//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal partial class CompositeSoundEditor
{
    private void OutlinerUI()
    {
        using (UI.BeginColumn(ElementId.OutlinerPanel, EditorStyle.Inspector.Root))
        {
            LayerListUI();
        }
    }

    private void LayerListUI()
    {
        void AddButton()
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(ElementId.AddLayerButton, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
            {
                AssetPalette.Open(
                    AssetType.Sound,
                    onPicked: doc =>
                    {
                        if (doc is SoundDocument soundDoc && !soundDoc.IsComposite)
                        {
                            Undo.Record(Document);
                            Document.AddLayer(soundDoc);
                            SelectedLayerIndex = Document.Layers.Count - 1;
                            Document.ApplyChanges();
                            RebuildLayerEditors();
                            FrameWaveform();
                        }
                    },
                    filter: doc => doc is SoundDocument sd && !sd.IsComposite && sd != Document);
            }
            ElementTree.EndAlign();
        }

        using (Inspector.BeginSection("LAYERS", content: AddButton))
        {
            if (Inspector.IsSectionCollapsed) return;

            for (var i = 0; i < Document.Layers.Count; i++)
            {
                var layer = Document.Layers[i];
                var isSelected = SelectedLayerIndex == i;
                var rowId = ElementId.LayerRow + i;

                var style = EditorStyle.Inspector.ListItem;
                if (isSelected)
                    style = style with { Background = EditorStyle.Palette.Active };

                using (UI.BeginRow(rowId, style))
                {
                    UI.Image(EditorAssets.Sprites.AssetIconSound, EditorStyle.Control.IconSecondary);
                    using (UI.BeginFlex())
                        UI.Text(layer.SoundRef.Name ?? "missing", EditorStyle.Text.Primary);
                }

                if (UI.WasPressed(rowId))
                {
                    UI.ClearHot();
                    SelectedLayerIndex = i;
                }
            }
        }
    }
}
