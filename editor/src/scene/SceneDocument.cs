//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Text;

namespace NoZ.Editor;

public partial class SceneDocument : Document
{
    public const string Extension = ".scene";

    public SceneGroup Root { get; } = new() { Name = "Root" };

    public override bool CanSave => true;

    public static void RegisterDef()
    {
        DocumentDef<SceneDocument>.Register(new DocumentDef
        {
            Type = AssetType.Scene,
            Name = "Scene",
            Extensions = [Extension],
            Factory = _ => new SceneDocument(),
            EditorFactory = doc => new SceneEditor((SceneDocument)doc),
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });
    }

    public static Document? CreateNew(string? name = null, Vector2? position = null)
    {
        return Project.New(AssetType.Scene, Extension, name, writer =>
        {
            writer.WriteLine("type scene");
        }, position);
    }

    public override void Load()
    {
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Root.Clear();
        Parse(ref tk);
        ResolveRefs();
        Loaded = true;
    }

    public override void Reload()
    {
        Root.Clear();
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Parse(ref tk);
        ResolveRefs();
    }

    public override void PostLoad() => ResolveRefs();

    private void ResolveRefs()
    {
        Root.ForEach(n =>
        {
            if (n is SceneSprite s)
                s.Sprite.Resolve();
        });
    }

    public override void OnRenamed(Document doc, string oldName, string newName)
    {
        if (doc is not SpriteDocument) return;

        var changed = false;
        Root.ForEach(n =>
        {
            if (n is SceneSprite s && s.Sprite.TryRename(oldName, newName))
                changed = true;
        });
        if (changed)
            IncrementVersion();
    }

    public override void GetDependencies(List<(AssetType Type, string Name)> dependencies)
    {
        Root.ForEach(n =>
        {
            if (n is SceneSprite s && !string.IsNullOrEmpty(s.Sprite.Name))
                dependencies.Add((AssetType.Sprite, s.Sprite.Name));
        });
    }

    public override void Clone(Document source)
    {
        var src = (SceneDocument)source;
        Root.Dispose();
        Root.Clear();
        foreach (var child in src.Root.Children)
            Root.Add(child.Clone());
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        // Build a flat node list with parent/child indexing
        var flat = new List<SceneNode>();
        var firstChild = new List<int>();
        var childCount = new List<int>();
        var rootIndices = new List<int>();

        // Two-pass: first assign indices in DFS order, then fill child ranges
        Flatten(Root, flat, firstChild, childCount, rootIndices, isRoot: true);

        using var stream = File.OpenWrite(outputPath);
        stream.SetLength(0);
        using var writer = new BinaryWriter(stream);
        writer.WriteAssetHeader(AssetType.Scene, Scene.Version);

        writer.Write(flat.Count);
        for (var i = 0; i < flat.Count; i++)
        {
            var node = flat[i];
            WriteString(writer, node.Name);
            writer.Write(node.Position.X);
            writer.Write(node.Position.Y);
            writer.Write(node.Rotation);
            writer.Write(node.Scale.X);
            writer.Write(node.Scale.Y);
            writer.Write(node.Color.R);
            writer.Write(node.Color.G);
            writer.Write(node.Color.B);
            writer.Write(node.Color.A);

            var spriteName = node is SceneSprite s && !node.Placeholder ? s.Sprite.Name ?? "" : "";
            WriteString(writer, spriteName);
            writer.Write(node.Placeholder);

            writer.Write(firstChild[i]);
            writer.Write(childCount[i]);
        }

        writer.Write(rootIndices.Count);
        foreach (var ri in rootIndices)
            writer.Write(ri);
    }

    private static int Flatten(
        SceneNode node,
        List<SceneNode> flat,
        List<int> firstChild,
        List<int> childCount,
        List<int> rootIndices,
        bool isRoot)
    {
        if (isRoot)
        {
            // Skip the synthetic Root group itself, just process its children
            foreach (var child in node.Children)
            {
                var idx = Flatten(child, flat, firstChild, childCount, rootIndices, isRoot: false);
                rootIndices.Add(idx);
            }
            return -1;
        }

        var index = flat.Count;
        flat.Add(node);
        firstChild.Add(0);
        childCount.Add(0);

        var first = flat.Count;
        foreach (var child in node.Children)
            Flatten(child, flat, firstChild, childCount, rootIndices, isRoot: false);

        firstChild[index] = first;
        childCount[index] = node.Children.Count;
        return index;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.Write(0);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    public override void Draw()
    {
        DrawOrigin();
        using (Graphics.PushState())
        {
            Graphics.SetTransform(Transform);
            DrawNodeTree(Root, Matrix3x2.Identity, Color.White);
        }
    }

    internal void DrawNodeTree(SceneNode node, Matrix3x2 parent, Color parentTint)
    {
        var local = node == Root ? Matrix3x2.Identity : node.LocalTransform;
        var world = local * parent;
        var nodeTint = node.Color.ToColor();
        var tint = new Color(
            parentTint.R * nodeTint.R,
            parentTint.G * nodeTint.G,
            parentTint.B * nodeTint.B,
            parentTint.A * nodeTint.A);

        if (node is SceneSprite s && s.Visible && s.Sprite.Value?.Sprite is { } sprite)
        {
            using (Graphics.PushState())
            {
                Graphics.SetTransform(world * Transform);
                Graphics.SetColor(tint);
                Graphics.SetTextureFilter(s.Sprite.Value.TextureFilter);
                Graphics.SetShader(EditorAssets.Shaders.Sprite);
                Graphics.Draw(sprite);
            }
        }

        if (node.Visible)
        {
            foreach (var child in node.Children)
                DrawNodeTree(child, world, tint);
        }
    }

    public override Rect Bounds
    {
        get
        {
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            var any = false;
            Root.ForEach(n =>
            {
                if (n is not SceneSprite s) return;
                if (s.Sprite.Value is not { } doc) return;
                var b = doc.Bounds;
                var w = s.WorldTransform;
                Span<Vector2> corners = stackalloc Vector2[]
                {
                    Vector2.Transform(new Vector2(b.X, b.Y), w),
                    Vector2.Transform(new Vector2(b.Right, b.Y), w),
                    Vector2.Transform(new Vector2(b.Right, b.Bottom), w),
                    Vector2.Transform(new Vector2(b.X, b.Bottom), w),
                };
                foreach (var c in corners)
                {
                    min = Vector2.Min(min, c);
                    max = Vector2.Max(max, c);
                }
                any = true;
            });
            return any ? Rect.FromMinMax(min, max) : new Rect(-0.5f, -0.5f, 1f, 1f);
        }
        set { /* computed */ }
    }

    public override void Dispose()
    {
        Root.Dispose();
        base.Dispose();
    }
}
