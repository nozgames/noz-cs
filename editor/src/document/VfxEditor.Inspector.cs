//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Linq;

namespace NoZ.Editor;

internal partial class VfxEditor
{
    // Fixed field ID offsets — deterministic regardless of which sections are open
    // RangeField uses 3 IDs: min, random, max
    // IntRangeField uses 3 IDs: min, random, max
    // FloatCurveField uses 7 IDs: startMin, startRandom, startMax, endMin, endRandom, endMax, curveType
    // ColorCurveField uses 7 IDs: startMin, startRandom, startMax, endMin, endRandom, endMax, curveType
    // Vec2RangeField uses 4 IDs: minX, minY, maxX, maxY
    private static partial class FieldId
    {
        public static partial WidgetId VfxDuration { get; }       // +0..+2
        public static partial WidgetId VfxLoop { get; }

        public static partial WidgetId EmitterRate { get; }       // +0..+2
        public static partial WidgetId EmitterBurst { get; }      // +0..+2
        public static partial WidgetId EmitterDuration { get; }   // +0..+2
        public static partial WidgetId EmitterWorldSpace { get; }
        public static partial WidgetId EmitterParticle { get; }
        public static partial WidgetId EmitterAngle { get; }      // +0..+2
        public static partial WidgetId EmitterSpawn { get; }      // +0..+3
        public static partial WidgetId EmitterDirection { get; }  // +0..+3

        public static partial WidgetId ParticleDuration { get; }      // +0..+2
        public static partial WidgetId ParticleSize { get; }          // +0..+6
        public static partial WidgetId ParticleSpeed { get; }         // +0..+6
        public static partial WidgetId ParticleColor { get; }         // +0..+6
        public static partial WidgetId ParticleOpacity { get; }       // +0..+6
        public static partial WidgetId ParticleGravity { get; }       // +0..+3
        public static partial WidgetId ParticleDrag { get; }          // +0..+2
        public static partial WidgetId ParticleRotation { get; }      // +0..+2
        public static partial WidgetId ParticleRotationSpeed { get; } // +0..+6

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
            () => { }, // just show the section, user picks from asset palette
            () => { particle.SpriteRef.Clear(); }))
        {
            using (Inspector.BeginProperty("Sprite"))
            {
                particle.SpriteRef = EditorUI.SpriteButton(FieldId.SpriteDropDown, particle.SpriteRef);
                UI.HandleChange(Document);
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

    private static readonly ContainerStyle ValueRowStyle = new() { Spacing = 4, Height = Size.Fit, MinHeight = EditorStyle.Control.Height };
    private static readonly ContainerStyle CurveRowStyle = new() { Spacing = 4, Height = Size.Fit, MinHeight = 22 };

    // Range field: [label] [min] [⇄] [max]  — no curve row
    private static bool RangeField(WidgetId baseId, string label, ref VfxRange value)
    {
        var changed = false;
        using (Inspector.BeginProperty(label))
        using (UI.BeginRow(ValueRowStyle))
        {
            if (FloatInput(baseId, ref value.Min)) changed = true;
            if (RandomToggle(baseId + 1, value.Min != value.Max, ref value.Min, ref value.Max)) changed = true;
            if (value.Min != value.Max)
            {
                if (FloatInput(baseId + 2, ref value.Max)) changed = true;
            }
            else
                using (UI.BeginFlex()) { } // reserve space
        }
        return changed;
    }

    // Int range field: [label] [min] [⇄] [max]  — no curve row
    private static bool IntRangeField(WidgetId baseId, string label, ref VfxIntRange value)
    {
        var changed = false;
        using (Inspector.BeginProperty(label))
        using (UI.BeginRow(ValueRowStyle))
        {
            if (IntInput(baseId, ref value.Min)) changed = true;
            if (RandomToggleInt(baseId + 1, value.Min != value.Max, ref value.Min, ref value.Max)) changed = true;
            if (value.Min != value.Max)
            {
                if (IntInput(baseId + 2, ref value.Max)) changed = true;
            }
            else
                using (UI.BeginFlex()) { }
        }
        return changed;
    }

    // Vec2 range field: two rows of min/max (no random toggle — too complex)
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

    // Float curve field with progressive disclosure
    // baseId+0: startMin, +1: startRandom, +2: startMax, +3: endMin, +4: endRandom, +5: endMax, +6: curveType
    private static bool FloatCurveField(WidgetId baseId, string label, ref VfxFloatCurve curve)
    {
        var changed = false;
        var hasCurve = curve.Start != curve.End;
        var startLabel = hasCurve ? "Start" : "Value";

        // Start/Value row
        using (Inspector.BeginProperty(startLabel))
        using (UI.BeginRow(ValueRowStyle))
        {
            if (FloatInput(baseId, ref curve.Start.Min)) changed = true;
            if (RandomToggle(baseId + 1, curve.Start.Min != curve.Start.Max, ref curve.Start.Min, ref curve.Start.Max)) changed = true;
            if (curve.Start.Min != curve.Start.Max)
            {
                if (FloatInput(baseId + 2, ref curve.Start.Max)) changed = true;
            }
            else
                using (UI.BeginFlex()) { }
        }

        // End row (only when curve active)
        if (hasCurve)
        {
            using (Inspector.BeginProperty("End"))
            using (UI.BeginRow(ValueRowStyle))
            {
                if (FloatInput(baseId + 3, ref curve.End.Min)) changed = true;
                if (RandomToggle(baseId + 4, curve.End.Min != curve.End.Max, ref curve.End.Min, ref curve.End.Max)) changed = true;
                if (curve.End.Min != curve.End.Max)
                {
                    if (FloatInput(baseId + 5, ref curve.End.Max)) changed = true;
                }
                else
                    using (UI.BeginFlex()) { }
            }
        }

        // Curve type row (always visible)
        if (CurveRow(baseId + 6, hasCurve, ref curve.Type, ref curve.Start, ref curve.End))
            changed = true;

        return changed;
    }

    // Color curve field with progressive disclosure
    private static bool ColorCurveField(WidgetId baseId, string label, ref VfxColorCurve curve)
    {
        var changed = false;
        var hasCurve = curve.Start != curve.End;
        var startLabel = hasCurve ? "Start" : "Value";

        // Start/Value row
        using (Inspector.BeginProperty(startLabel))
        using (UI.BeginRow(ValueRowStyle))
        {
            using (UI.BeginFlex())
                if (ColorInput(baseId, ref curve.Start.Min)) changed = true;
            if (ColorRandomToggle(baseId + 1, curve.Start.Min != curve.Start.Max, ref curve.Start.Min, ref curve.Start.Max)) changed = true;
            if (curve.Start.Min != curve.Start.Max)
            {
                using (UI.BeginFlex())
                    if (ColorInput(baseId + 2, ref curve.Start.Max)) changed = true;
            }
            else
                using (UI.BeginFlex()) { }
        }

        // End row
        if (hasCurve)
        {
            using (Inspector.BeginProperty("End"))
            using (UI.BeginRow(ValueRowStyle))
            {
                using (UI.BeginFlex())
                    if (ColorInput(baseId + 3, ref curve.End.Min)) changed = true;
                if (ColorRandomToggle(baseId + 4, curve.End.Min != curve.End.Max, ref curve.End.Min, ref curve.End.Max)) changed = true;
                if (curve.End.Min != curve.End.Max)
                {
                    using (UI.BeginFlex())
                        if (ColorInput(baseId + 5, ref curve.End.Max)) changed = true;
                }
                else
                    using (UI.BeginFlex()) { }
            }
        }

        // Curve type row
        if (ColorCurveRow(baseId + 6, hasCurve, ref curve.Type, ref curve.Start, ref curve.End))
            changed = true;

        return changed;
    }

    // --- Curve Row ---

    // Curve type dropdown row for float curves. Returns true if changed.
    // When "None" selected: copies start to end. When type selected from None: keeps end as-is (user will edit).
    private static bool CurveRow(WidgetId id, bool hasCurve, ref VfxCurveType type, ref VfxRange start, ref VfxRange end)
    {
        using (Inspector.BeginProperty(""))
        using (UI.BeginRow(CurveRowStyle))
        {
            var oldHasCurve = hasCurve;
            var oldType = type;

            if (CurveTypeDropdown(id, ref type, ref hasCurve))
            {
                if (!hasCurve)
                    end = start; // "None" selected — collapse curve
                return true;
            }
        }
        return false;
    }

    private static bool ColorCurveRow(WidgetId id, bool hasCurve, ref VfxCurveType type, ref VfxColorRange start, ref VfxColorRange end)
    {
        using (Inspector.BeginProperty(""))
        using (UI.BeginRow(CurveRowStyle))
        {
            if (CurveTypeDropdown(id, ref type, ref hasCurve))
            {
                if (!hasCurve)
                    end = start;
                return true;
            }
        }
        return false;
    }

    // --- Random Toggle ---

    private static readonly ButtonStyle RandomButtonStyle = new()
    {
        Width = 21,
        Height = 21,
        Background = EditorStyle.Palette.Canvas,
        ContentColor = EditorStyle.Palette.SecondaryText,
        IconSize = 11,
        BorderRadius = EditorStyle.Control.BorderRadius,
        Resolve = (s, f) =>
        {
            if ((f & WidgetFlags.Hovered) != 0) s.Background = EditorStyle.Palette.Active;
            return s;
        },
    };

    private static readonly ButtonStyle RandomButtonActiveStyle = RandomButtonStyle with
    {
        Background = EditorStyle.Palette.Active,
        ContentColor = EditorStyle.Palette.Content,
    };

    private static bool RandomToggle(WidgetId id, bool isRandom, ref float min, ref float max)
    {
        var style = isRandom ? RandomButtonActiveStyle : RandomButtonStyle;
        if (UI.Button(id, EditorAssets.Sprites.IconRandomRange, style))
        {
            if (isRandom)
                max = min; // collapse
            else
                max = min + MathF.Max(MathF.Abs(min) * 0.5f, 0.1f); // expand with reasonable spread
            return true;
        }
        return false;
    }

    private static bool RandomToggleInt(WidgetId id, bool isRandom, ref int min, ref int max)
    {
        var style = isRandom ? RandomButtonActiveStyle : RandomButtonStyle;
        if (UI.Button(id, EditorAssets.Sprites.IconRandomRange, style))
        {
            if (isRandom)
                max = min;
            else
                max = min + Math.Max(Math.Abs(min) / 2, 1);
            return true;
        }
        return false;
    }

    private static bool ColorRandomToggle(WidgetId id, bool isRandom, ref Color min, ref Color max)
    {
        var style = isRandom ? RandomButtonActiveStyle : RandomButtonStyle;
        if (UI.Button(id, EditorAssets.Sprites.IconRandomRange, style))
        {
            if (isRandom)
                max = min;
            else
                max = min; // user picks the second color via color picker
            return true;
        }
        return false;
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

    // --- Curve Type Dropdown ---

    private static readonly (string Name, VfxCurveType Type)[] CurveTypeOptions =
        Enum.GetValues<VfxCurveType>()
            .Where(t => t != VfxCurveType.CubicBezier)
            .Select(t => (Enum.GetName(t)!, t))
            .ToArray();

    private static bool _curveChanged;
    private static VfxCurveType _curveNewType;
    private static bool _curveNewHasCurve;
    private static WidgetId _curveChangedId;

    private static bool CurveTypeDropdown(WidgetId id, ref VfxCurveType curveType, ref bool hasCurve)
    {
        // Check first: the popup handler fires AFTER this method during PopupMenu.UpdateUI()
        if (_curveChanged && _curveChangedId == id)
        {
            _curveChanged = false;
            curveType = _curveNewType;
            hasCurve = _curveNewHasCurve;
            return true;
        }

        var currentName = "None";
        if (hasCurve)
        {
            foreach (var opt in CurveTypeOptions)
                if (opt.Type == curveType) { currentName = opt.Name; break; }
        }

        UI.DropDown(id, () =>
        {
            var items = new PopupMenuItem[CurveTypeOptions.Length + 1];
            items[0] = PopupMenuItem.Item("None", () =>
            {
                _curveChanged = true;
                _curveChangedId = id;
                _curveNewHasCurve = false;
                _curveNewType = VfxCurveType.Linear;
            });
            for (var i = 0; i < CurveTypeOptions.Length; i++)
            {
                var opt = CurveTypeOptions[i];
                items[i + 1] = PopupMenuItem.Item(opt.Name, () =>
                {
                    _curveChanged = true;
                    _curveChangedId = id;
                    _curveNewHasCurve = true;
                    _curveNewType = opt.Type;
                });
            }
            return items;
        }, text: currentName);

        return false;
    }

}
