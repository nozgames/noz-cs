//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class SpriteInstanceDocument : Document
{
    public const string Extension = ".instance";

    private static partial class WidgetIds
    {
        public static partial WidgetId SourceField { get; }
        public static partial WidgetId FlipXToggle { get; }
        public static partial WidgetId FlipYToggle { get; }
        public static partial WidgetId RotationDropDown { get; }
    }

    public DocumentRef<SpriteDocument> Source;
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }

    private int _rotation;
    public int Rotation
    {
        get => _rotation;
        set => _rotation = NormalizeRotation(value);
    }

    public override bool CanSave => true;
    public override bool CanExport => false;

    public static void RegisterDef()
    {
        DocumentDef<SpriteInstanceDocument>.Register(new DocumentDef
        {
            Type = AssetType.Sprite,
            Name = "SpriteInstance",
            Extensions = [Extension],
            Factory = _ => new SpriteInstanceDocument(),
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });
    }

    public static Document? CreateNew(SpriteDocument sourceSprite, Vector2? position = null)
    {
        var sourceName = sourceSprite.Name;
        return Project.New(AssetType.Sprite, Extension, sourceName, writer =>
        {
            writer.WriteLine($"source \"{sourceName}\"");
        }, position);
    }

    public override void Load()
    {
        var text = File.ReadAllText(Path);
        var tokenizer = new Tokenizer(text);

        Source = default;
        FlipX = false;
        FlipY = false;
        _rotation = 0;

        while (!tokenizer.IsEOF)
        {
            if (!tokenizer.ExpectIdentifier(out var keyword))
            {
                tokenizer.Skip();
                continue;
            }

            switch (keyword)
            {
                case "source":
                    var name = tokenizer.ExpectQuotedString() ?? "";
                    Source = new DocumentRef<SpriteDocument> { Name = name };
                    break;
                case "flip_x":
                    FlipX = true;
                    break;
                case "flip_y":
                    FlipY = true;
                    break;
                case "rotation":
                    _rotation = NormalizeRotation(tokenizer.ExpectInt());
                    break;
                default:
                    tokenizer.Skip();
                    break;
            }
        }
    }

    public override void Save(StreamWriter sw)
    {
        if (Source.HasValue)
            sw.WriteLine($"source \"{Source.Name}\"");
        if (FlipX)
            sw.WriteLine("flip_x");
        if (FlipY)
            sw.WriteLine("flip_y");
        if (_rotation != 0)
            sw.WriteLine($"rotation {_rotation}");
    }

    public override void PostLoad()
    {
        Source.Resolve();
    }

    public override Rect Bounds
    {
        get
        {
            var src = Source.Value?.Bounds ?? new Rect(-0.5f, -0.5f, 1f, 1f);
            var m = LocalTransform;
            var c1 = Vector2.Transform(new Vector2(src.X, src.Y), m);
            var c2 = Vector2.Transform(new Vector2(src.X + src.Width, src.Y), m);
            var c3 = Vector2.Transform(new Vector2(src.X, src.Y + src.Height), m);
            var c4 = Vector2.Transform(new Vector2(src.X + src.Width, src.Y + src.Height), m);
            var minX = MathF.Min(MathF.Min(c1.X, c2.X), MathF.Min(c3.X, c4.X));
            var minY = MathF.Min(MathF.Min(c1.Y, c2.Y), MathF.Min(c3.Y, c4.Y));
            var maxX = MathF.Max(MathF.Max(c1.X, c2.X), MathF.Max(c3.X, c4.X));
            var maxY = MathF.Max(MathF.Max(c1.Y, c2.Y), MathF.Max(c3.Y, c4.Y));
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
        set { }
    }

    private Matrix3x2 LocalTransform =>
        Matrix3x2.CreateScale(FlipX ? -1f : 1f, FlipY ? -1f : 1f)
        * Matrix3x2.CreateRotation(_rotation * MathF.PI / 180f);

    public override void Draw()
    {
        DrawOrigin();

        var srcDoc = Source.Value;
        var srcSprite = srcDoc?.Sprite;
        if (srcSprite == null)
        {
            DrawBounds();
            return;
        }

        using (Graphics.PushState())
        {
            Graphics.SetTextureFilter(srcDoc!.TextureFilter);
            Graphics.SetShader(EditorAssets.Shaders.Sprite);
            Graphics.SetColor(Color.White.WithAlpha(Workspace.XrayAlpha));

            Graphics.SetTransform(LocalTransform * Graphics.Transform);
            Graphics.Draw(srcSprite, order: 0);
        }
    }

    public override bool DrawThumbnail()
    {
        var srcSprite = Source.Value?.Sprite;
        if (srcSprite == null)
            return false;

        UI.Image(srcSprite, ImageStyle.Center);
        return true;
    }

    public override void GetReferences(List<Document> references)
    {
        if (Source.IsResolved)
            references.Add(Source.Value!);
    }

    public override void OnRenamed(Document doc, string oldName, string newName)
    {
        if (doc is not SpriteDocument) return;
        if (Source.TryRename(oldName, newName))
            IncrementVersion();
    }

    public override void Clone(Document source)
    {
        var src = (SpriteInstanceDocument)source;
        Source = src.Source;
        FlipX = src.FlipX;
        FlipY = src.FlipY;
        _rotation = src._rotation;
    }

    public override void InspectorUI()
    {
        if (Inspector.IsSectionCollapsed) return;

        using (Inspector.BeginProperty("Source"))
        {
            var newRef = EditorUI.SpriteField(WidgetIds.SourceField, Source);
            if (newRef.Value != Source.Value)
            {
                Undo.Record(this);
                Source = newRef;
            }
        }

        using (Inspector.BeginProperty("Flip X"))
        {
            if (UI.Toggle(WidgetIds.FlipXToggle, FlipX, EditorStyle.Inspector.Toggle))
            {
                Undo.Record(this);
                FlipX = !FlipX;
            }
        }

        using (Inspector.BeginProperty("Flip Y"))
        {
            if (UI.Toggle(WidgetIds.FlipYToggle, FlipY, EditorStyle.Inspector.Toggle))
            {
                Undo.Record(this);
                FlipY = !FlipY;
            }
        }

        using (Inspector.BeginProperty("Rotation"))
        {
            UI.DropDown(WidgetIds.RotationDropDown, () =>
            [
                ..new[] { 0, 90, 180, 270 }.Select(deg => new PopupMenuItem
                {
                    Label = $"{deg}°",
                    Handler = () =>
                    {
                        Undo.Record(this);
                        _rotation = deg;
                    }
                })
            ], $"{_rotation}°");
        }
    }

    private static int NormalizeRotation(int value)
    {
        var v = value % 360;
        if (v < 0) v += 360;
        var snapped = ((v + 45) / 90) * 90;
        return snapped % 360;
    }
}
