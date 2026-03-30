//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal partial class VfxEditor
{
    // Fixed field ID offsets — deterministic regardless of which sections are open
    private static partial class FieldId
    {
        // VFX global (0-9)
        public static partial WidgetId VfxDuration { get; }      // +0,+1
        public static partial WidgetId VfxLoop { get; }           // +0

        // Emitter (10-39)
        public static partial WidgetId EmitterRate { get; }       // +0,+1
        public static partial WidgetId EmitterBurst { get; }      // +0,+1
        public static partial WidgetId EmitterDuration { get; }   // +0,+1
        public static partial WidgetId EmitterWorldSpace { get; } // +0
        public static partial WidgetId EmitterParticle { get; }   // +0
        public static partial WidgetId EmitterAngle { get; }      // +0,+1
        public static partial WidgetId EmitterSpawn { get; }      // +0,+1,+2,+3
        public static partial WidgetId EmitterDirection { get; }  // +0,+1,+2,+3

        // Particle (40-99)
        public static partial WidgetId ParticleDuration { get; }      // +0,+1
        public static partial WidgetId ParticleSize { get; }          // +0..+4
        public static partial WidgetId ParticleSpeed { get; }         // +0..+4
        public static partial WidgetId ParticleColor { get; }         // +0..+4
        public static partial WidgetId ParticleOpacity { get; }       // +0..+4
        public static partial WidgetId ParticleGravity { get; }       // +0..+3
        public static partial WidgetId ParticleDrag { get; }          // +0,+1
        public static partial WidgetId ParticleRotation { get; }      // +0,+1
        public static partial WidgetId ParticleRotationSpeed { get; } // +0..+4

        // Addable section buttons
        public static partial WidgetId AddAngle { get; }
        public static partial WidgetId AddSpawn { get; }
        public static partial WidgetId AddDirection { get; }
        public static partial WidgetId AddSize { get; }
        public static partial WidgetId AddSpeed { get; }
        public static partial WidgetId AddColor { get; }
        public static partial WidgetId AddOpacity { get; }
        public static partial WidgetId AddGravity { get; }
        public static partial WidgetId AddDrag { get; }
        public static partial WidgetId AddRotation { get; }
        public static partial WidgetId AddSprite { get; }
        public static partial WidgetId RemoveAngle { get; }
        public static partial WidgetId RemoveSpawn { get; }
        public static partial WidgetId RemoveDirection { get; }
        public static partial WidgetId RemoveSize { get; }
        public static partial WidgetId RemoveSpeed { get; }
        public static partial WidgetId RemoveColor { get; }
        public static partial WidgetId RemoveOpacity { get; }
        public static partial WidgetId RemoveGravity { get; }
        public static partial WidgetId RemoveDrag { get; }
        public static partial WidgetId RemoveRotation { get; }
        public static partial WidgetId RemoveSprite { get; }
        public static partial WidgetId SpriteDropDown { get; }
    }

    public override void InspectorUI()
    {
        switch (Document.SelectedType)
        {
            case VfxSelectionType.Vfx:
                VfxGlobalUI();
                break;

            case VfxSelectionType.Emitter:
                if (Document.SelectedIndex >= 0 && Document.SelectedIndex < Document.Emitters.Count)
                    EmitterInspectorUI(Document.Emitters[Document.SelectedIndex]);
                break;

            case VfxSelectionType.Particle:
                if (Document.SelectedIndex >= 0 && Document.SelectedIndex < Document.Particles.Count)
                    ParticleInspectorUI(Document.Particles[Document.SelectedIndex]);
                break;
        }
    }

    // --- VFX Global ---

    private void VfxGlobalUI()
    {
        using (Inspector.BeginSection("VFX"))
        {
            if (Inspector.IsSectionCollapsed) return;

            var duration = Document.Duration;
            if (RangeField(FieldId.VfxDuration, "Duration", ref duration))
            {
                Undo.Record(Document);
                Document.Duration = duration;
                Document.ApplyChanges();
            }

            var loop = Document.Loop;
            using (Inspector.BeginProperty("Loop"))
            {
                if (UI.Toggle(FieldId.VfxLoop, "", loop, EditorStyle.Inspector.Toggle, EditorAssets.Sprites.IconCheck))
                {
                    Undo.Record(Document);
                    Document.Loop = !loop;
                    Document.ApplyChanges();
                }
            }
        }
    }

    // --- Emitter Inspector ---

    private void EmitterInspectorUI(VfxDocEmitter emitter)
    {
        using (Inspector.BeginSection("EMITTER"))
        {
            if (Inspector.IsSectionCollapsed) return;

            var changed = false;

            if (IntRangeField(FieldId.EmitterRate, "Rate", ref emitter.Def.Rate)) changed = true;
            if (IntRangeField(FieldId.EmitterBurst, "Burst", ref emitter.Def.Burst)) changed = true;
            if (RangeField(FieldId.EmitterDuration, "Duration", ref emitter.Def.Duration)) changed = true;

            using (Inspector.BeginProperty("WorldSpace"))
            {
                if (UI.Toggle(FieldId.EmitterWorldSpace, "", emitter.Def.WorldSpace, EditorStyle.Inspector.Toggle, EditorAssets.Sprites.IconCheck))
                {
                    emitter.Def.WorldSpace = !emitter.Def.WorldSpace;
                    changed = true;
                }
            }

            // Particle dropdown
            using (Inspector.BeginProperty("Particle"))
            {
                var currentName = emitter.ParticleRef;
                UI.DropDown(FieldId.EmitterParticle, () =>
                {
                    var items = new List<PopupMenuItem>();
                    foreach (var p in Document.Particles)
                    {
                        var name = p.Name;
                        items.Add(PopupMenuItem.Item(name, () =>
                        {
                            Undo.Record(Document);
                            emitter.ParticleRef = name;
                            Document.ApplyChanges();
                        }));
                    }
                    return [.. items];
                }, text: currentName);
            }

            if (changed)
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
        }

        // Addable emitter groups
        if (AddableSection("ANGLE", emitter.Def.Angle != default, FieldId.AddAngle, FieldId.RemoveAngle,
            () => { emitter.Def.Angle = new VfxRange(0, 360); },
            () => { emitter.Def.Angle = default; }))
        {
            if (RangeField(FieldId.EmitterAngle, "Angle", ref emitter.Def.Angle))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("SPAWN", emitter.Def.Spawn != VfxVec2Range.Zero, FieldId.AddSpawn, FieldId.RemoveSpawn,
            () => { emitter.Def.Spawn = VfxVec2Range.Zero; },
            () => { emitter.Def.Spawn = VfxVec2Range.Zero; }))
        {
            if (Vec2RangeField(FieldId.EmitterSpawn, "Spawn", ref emitter.Def.Spawn))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("DIRECTION", emitter.Def.Direction != VfxVec2Range.Zero, FieldId.AddDirection, FieldId.RemoveDirection,
            () => { emitter.Def.Direction = new VfxVec2Range(new(0, -1), new(0, -1)); },
            () => { emitter.Def.Direction = VfxVec2Range.Zero; }))
        {
            if (Vec2RangeField(FieldId.EmitterDirection, "Direction", ref emitter.Def.Direction))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }
    }

    // --- Particle Inspector ---

    private void ParticleInspectorUI(VfxDocParticle particle)
    {
        using (Inspector.BeginSection("PARTICLE"))
        {
            if (Inspector.IsSectionCollapsed) return;

            if (RangeField(FieldId.ParticleDuration, "Duration", ref particle.Def.Duration))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
        }

        // Addable particle groups
        if (AddableSection("SIZE", particle.Def.Size != VfxFloatCurve.One, FieldId.AddSize, FieldId.RemoveSize,
            () => { particle.Def.Size = new VfxFloatCurve { Type = VfxCurveType.EaseOut, Start = new VfxRange(0.5f, 0.5f), End = new VfxRange(0f, 0.1f) }; },
            () => { particle.Def.Size = VfxFloatCurve.One; }))
        {
            if (FloatCurveField(FieldId.ParticleSize, "Size", ref particle.Def.Size))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("SPEED", particle.Def.Speed != VfxFloatCurve.Zero, FieldId.AddSpeed, FieldId.RemoveSpeed,
            () => { particle.Def.Speed = new VfxFloatCurve { Type = VfxCurveType.Linear, Start = new VfxRange(10, 20), End = new VfxRange(0, 5) }; },
            () => { particle.Def.Speed = VfxFloatCurve.Zero; }))
        {
            if (FloatCurveField(FieldId.ParticleSpeed, "Speed", ref particle.Def.Speed))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("COLOR", particle.Def.Color != VfxColorCurve.White, FieldId.AddColor, FieldId.RemoveColor,
            () => { particle.Def.Color = new VfxColorCurve { Type = VfxCurveType.Linear, Start = new VfxColorRange(Color.White, Color.White), End = new VfxColorRange(Color.Yellow, Color.Yellow) }; },
            () => { particle.Def.Color = VfxColorCurve.White; }))
        {
            if (ColorCurveField(FieldId.ParticleColor, "Color", ref particle.Def.Color))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("OPACITY", particle.Def.Opacity != VfxFloatCurve.One, FieldId.AddOpacity, FieldId.RemoveOpacity,
            () => { particle.Def.Opacity = new VfxFloatCurve { Type = VfxCurveType.EaseOut, Start = VfxRange.One, End = VfxRange.Zero }; },
            () => { particle.Def.Opacity = VfxFloatCurve.One; }))
        {
            if (FloatCurveField(FieldId.ParticleOpacity, "Opacity", ref particle.Def.Opacity))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("GRAVITY", particle.Def.Gravity != VfxVec2Range.Zero, FieldId.AddGravity, FieldId.RemoveGravity,
            () => { particle.Def.Gravity = new VfxVec2Range(new(0, 10), new(0, 10)); },
            () => { particle.Def.Gravity = VfxVec2Range.Zero; }))
        {
            if (Vec2RangeField(FieldId.ParticleGravity, "Gravity", ref particle.Def.Gravity))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("DRAG", particle.Def.Drag != VfxRange.Zero, FieldId.AddDrag, FieldId.RemoveDrag,
            () => { particle.Def.Drag = new VfxRange(1, 1); },
            () => { particle.Def.Drag = VfxRange.Zero; }))
        {
            if (RangeField(FieldId.ParticleDrag, "Drag", ref particle.Def.Drag))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("ROTATION", particle.Def.Rotation != VfxRange.Zero || particle.Def.RotationSpeed != VfxFloatCurve.Zero,
            FieldId.AddRotation, FieldId.RemoveRotation,
            () => { particle.Def.Rotation = new VfxRange(0, 360); },
            () => { particle.Def.Rotation = VfxRange.Zero; particle.Def.RotationSpeed = VfxFloatCurve.Zero; }))
        {
            var changed = false;
            if (RangeField(FieldId.ParticleRotation, "Initial", ref particle.Def.Rotation)) changed = true;
            if (FloatCurveField(FieldId.ParticleRotationSpeed, "Speed", ref particle.Def.RotationSpeed)) changed = true;
            if (changed)
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("SPRITE", particle.SpriteRef.HasValue, FieldId.AddSprite, FieldId.RemoveSprite,
            () => { }, // just show the section, user picks from dropdown
            () => { particle.SpriteRef.Clear(); }))
        {
            using (Inspector.BeginProperty("Sprite"))
            {
                var currentName = particle.SpriteRef.Name ?? "None";
                UI.DropDown(FieldId.SpriteDropDown, () =>
                {
                    var items = new List<PopupMenuItem>
                    {
                        PopupMenuItem.Item("None", () =>
                        {
                            Undo.Record(Document);
                            particle.SpriteRef.Clear();
                            Document.ApplyChanges();
                        })
                    };
                    foreach (var doc in DocumentManager.Documents)
                    {
                        if (doc is SpriteDocument sprite)
                        {
                            var name = sprite.Name;
                            items.Add(PopupMenuItem.Item(name, () =>
                            {
                                Undo.Record(Document);
                                particle.SpriteRef = sprite;
                                Document.ApplyChanges();
                            }));
                        }
                    }
                    return [.. items];
                }, text: currentName);
            }
            EndAddableSection();
        }
    }

    // --- Addable Section Helper ---

    private Inspector.AutoSection _addableSectionHandle;

    private bool AddableSection(string name, bool isActive, WidgetId addId, WidgetId removeId, Action onAdd, Action onRemove)
    {
        if (!isActive)
        {
            using (Inspector.BeginSection(name, content: () =>
            {
                ElementTree.BeginAlign(Align.Min, Align.Center);
                if (UI.Button(addId, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
                {
                    Undo.Record(Document);
                    onAdd();
                    Document.ApplyChanges();
                }
                ElementTree.EndAlign();
            }, empty: true)) { }
            return false;
        }

        _addableSectionHandle = Inspector.BeginSection(name, content: () =>
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(removeId, EditorAssets.Sprites.IconDelete, EditorStyle.Inspector.SectionButton))
            {
                Undo.Record(Document);
                onRemove();
                Document.ApplyChanges();
            }
            ElementTree.EndAlign();
        });

        if (Inspector.IsSectionCollapsed)
        {
            ((IDisposable)_addableSectionHandle).Dispose();
            return false;
        }

        return true;
    }

    private void EndAddableSection()
    {
        ((IDisposable)_addableSectionHandle).Dispose();
    }

    // --- Field Helpers ---

    private static bool RangeField(WidgetId baseId, string label, ref VfxRange value)
    {
        var changed = false;
        using (Inspector.BeginProperty(label))
        using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
        {
            if (FloatInput(baseId, ref value.Min)) changed = true;
            if (FloatInput(baseId + 1, ref value.Max)) changed = true;
        }
        return changed;
    }

    private static bool IntRangeField(WidgetId baseId, string label, ref VfxIntRange value)
    {
        var changed = false;
        using (Inspector.BeginProperty(label))
        using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
        {
            if (IntInput(baseId, ref value.Min)) changed = true;
            if (IntInput(baseId + 1, ref value.Max)) changed = true;
        }
        return changed;
    }

    private static bool Vec2RangeField(WidgetId baseId, string label, ref VfxVec2Range value)
    {
        var changed = false;
        using (Inspector.BeginProperty(label))
        using (UI.BeginColumn(new ContainerStyle { Spacing = 2 }))
        {
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                if (FloatInput(baseId, ref value.Min.X)) changed = true;
                if (FloatInput(baseId + 1, ref value.Min.Y)) changed = true;
            }
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                if (FloatInput(baseId + 2, ref value.Max.X)) changed = true;
                if (FloatInput(baseId + 3, ref value.Max.Y)) changed = true;
            }
        }
        return changed;
    }

    private static bool FloatCurveField(WidgetId baseId, string label, ref VfxFloatCurve curve)
    {
        var changed = false;
        using (Inspector.BeginProperty(label))
        using (UI.BeginColumn(new ContainerStyle { Spacing = 2 }))
        {
            // Start range
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                if (FloatInput(baseId, ref curve.Start.Min)) changed = true;
                if (FloatInput(baseId + 1, ref curve.Start.Max)) changed = true;
            }
            // End range + curve type
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                if (FloatInput(baseId + 2, ref curve.End.Min)) changed = true;
                if (FloatInput(baseId + 3, ref curve.End.Max)) changed = true;
                if (CurveTypeDropdown(baseId + 4, ref curve.Type)) changed = true;
            }
        }
        return changed;
    }

    private static bool ColorCurveField(WidgetId baseId, string label, ref VfxColorCurve curve)
    {
        var changed = false;
        using (Inspector.BeginProperty(label))
        using (UI.BeginColumn(new ContainerStyle { Spacing = 2 }))
        {
            // Start color range
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                if (ColorInput(baseId, ref curve.Start.Min)) changed = true;
                if (ColorInput(baseId + 1, ref curve.Start.Max)) changed = true;
            }
            // End color range + curve type
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                if (ColorInput(baseId + 2, ref curve.End.Min)) changed = true;
                if (ColorInput(baseId + 3, ref curve.End.Max)) changed = true;
                if (CurveTypeDropdown(baseId + 4, ref curve.Type)) changed = true;
            }
        }
        return changed;
    }

    // --- Primitive Input Helpers ---

    private static bool FloatInput(WidgetId id, ref float value)
    {
        var text = VfxDocument.FormatFloat(value);
        bool changed = false;
        using (UI.BeginFlex())
        {
            var result = UI.TextInput(id, text, EditorStyle.Inspector.TextBox, "0");
            if (result != text && float.TryParse(result, out var parsed))
            {
                value = parsed;
                changed = true;
            }
        }
        return changed;
    }

    private static bool IntInput(WidgetId id, ref int value)
    {
        var text = value.ToString();
        bool changed = false;
        using (UI.BeginFlex())
        {
            var result = UI.TextInput(id, text, EditorStyle.Inspector.TextBox, "0");
            if (result != text && int.TryParse(result, out var parsed))
            {
                value = parsed;
                changed = true;
            }
        }
        return changed;
    }

    private static bool ColorInput(WidgetId id, ref Color color)
    {
        var color32 = color.ToColor32();
        if (EditorUI.ColorButton(id, ref color32))
        {
            color = color32.ToColor();
            return true;
        }
        return false;
    }

    private static readonly string[] CurveTypeNames =
        ["Linear", "EaseIn", "EaseOut", "EaseInOut", "Quadratic", "Cubic", "Sine", "Bell"];

    private static VfxCurveType _curveTypeValue;

    private static bool CurveTypeDropdown(WidgetId id, ref VfxCurveType curveType)
    {
        var oldType = curveType;
        _curveTypeValue = curveType;
        var typeName = (int)curveType < CurveTypeNames.Length ? CurveTypeNames[(int)curveType] : "Linear";

        void Content()
        {
            EditorUI.ControlText(typeName);
        }

        if (EditorUI.Control(id, Content, selected: EditorUI.IsPopupOpen(id), padding: true))
            EditorUI.TogglePopup(id);

        if (EditorUI.IsPopupOpen(id))
        {
            static void PopupContent()
            {
                for (int i = 0; i < CurveTypeNames.Length; i++)
                {
                    var type = (VfxCurveType)i;
                    if (EditorUI.PopupItem(CurveTypeNames[i], selected: _curveTypeValue == type))
                    {
                        _curveTypeValue = type;
                        EditorUI.ClosePopup();
                    }
                }
            }
            EditorUI.Popup(id, PopupContent);
        }

        curveType = _curveTypeValue;
        return curveType != oldType;
    }

}
