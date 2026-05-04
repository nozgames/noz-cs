//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Text;

namespace NoZ;

public class Scene : Asset
{
    internal const ushort Version = 2;

    public struct Node
    {
        public string Name;
        public Vector2 Position;
        public float Rotation;
        public Vector2 Scale;
        public Color32 Color;
        public string SpriteName;
        public Sprite? Sprite;
        public int FirstChild;
        public int ChildCount;
        public bool IsPlaceholder;
        public Matrix3x2 WorldTransform;

        public readonly Vector2 WorldPosition => new(WorldTransform.M31, WorldTransform.M32);
    }

    public Node[] Nodes { get; private set; } = [];
    public int[] RootNodes { get; private set; } = [];

    private Scene(string name) : base(AssetType.Scene, name) { }
    public Scene() : base(AssetType.Scene) { }

    public static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Scene, "Scene", typeof(Scene), Load, Version));
    }

    private static Scene? Load(Stream stream, string name)
    {
        var scene = new Scene(name);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        scene.Load(reader);
        return scene;
    }

    protected override void Load(BinaryReader reader)
    {
        var nodeCount = reader.ReadInt32();
        Nodes = new Node[nodeCount];

        for (var i = 0; i < nodeCount; i++)
        {
            ref var node = ref Nodes[i];
            var nameLen = reader.ReadInt32();
            node.Name = nameLen > 0 ? Encoding.UTF8.GetString(reader.ReadBytes(nameLen)) : "";
            node.Position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            node.Rotation = reader.ReadSingle();
            node.Scale = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            node.Color = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());

            var spriteNameLen = reader.ReadInt32();
            node.SpriteName = spriteNameLen > 0 ? Encoding.UTF8.GetString(reader.ReadBytes(spriteNameLen)) : "";
            node.IsPlaceholder = reader.ReadBoolean();
            node.FirstChild = reader.ReadInt32();
            node.ChildCount = reader.ReadInt32();

            if (!string.IsNullOrEmpty(node.SpriteName))
                node.Sprite = Get<Sprite>(AssetType.Sprite, node.SpriteName);
        }

        var rootCount = reader.ReadInt32();
        RootNodes = new int[rootCount];
        for (var i = 0; i < rootCount; i++)
            RootNodes[i] = reader.ReadInt32();

        for (var i = 0; i < RootNodes.Length; i++)
            ComputeWorldTransforms(RootNodes[i], Matrix3x2.Identity);
    }

    private void ComputeWorldTransforms(int index, Matrix3x2 parent)
    {
        ref var node = ref Nodes[index];
        var local = LocalMatrix(node);
        node.WorldTransform = local * parent;
        for (var c = 0; c < node.ChildCount; c++)
            ComputeWorldTransforms(node.FirstChild + c, node.WorldTransform);
    }

    private static Matrix3x2 LocalMatrix(in Node node) =>
        Matrix3x2.CreateScale(node.Scale) *
        Matrix3x2.CreateRotation(node.Rotation) *
        Matrix3x2.CreateTranslation(node.Position);

    public void Draw()
    {
        DrawWith(Matrix3x2.Identity, Color.White);
    }

    public void Draw(in Matrix3x2 root)
    {
        DrawWith(root, Color.White);
    }

    public void Draw(in Matrix3x2 root, in Color tint)
    {
        DrawWith(root, tint);
    }

    private void DrawWith(in Matrix3x2 root, in Color rootTint)
    {
        for (var i = 0; i < RootNodes.Length; i++)
            DrawNode(RootNodes[i], root, rootTint);
    }

    private void DrawNode(int index, in Matrix3x2 parent, in Color parentTint)
    {
        ref var node = ref Nodes[index];
        var local = LocalMatrix(node);
        var world = local * parent;
        var nodeTint = node.Color.ToColor();
        var tint = new Color(
            parentTint.R * nodeTint.R,
            parentTint.G * nodeTint.G,
            parentTint.B * nodeTint.B,
            parentTint.A * nodeTint.A);

        if (node.Sprite != null)
        {
            Graphics.SetTransform(world);
            Graphics.SetColor(tint);
            Graphics.Draw(node.Sprite);
        }

        for (var c = 0; c < node.ChildCount; c++)
            DrawNode(node.FirstChild + c, world, tint);
    }
}
