//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public readonly struct SpriteMesh(
    Rect uv,
    short order,
    short boneIndex = -1,
    Vector2Int offset = default,
    Vector2Int size = default)
{
    public readonly Rect UV = uv;
    public readonly short SortOrder = order;
    public readonly short BoneIndex = boneIndex;
    public readonly Vector2Int Offset = offset;
    public readonly Vector2Int Size = size;
}

public readonly struct SpriteFrameInfo(ushort meshStart, ushort meshCount)
{
    public readonly ushort MeshStart = meshStart;
    public readonly ushort MeshCount = meshCount;
}

public class Sprite : Asset
{
    public const ushort Version = 10;
    public const int MaxFrames = 64;

    public RectInt Bounds { get; private set; }
    public Vector2Int Size => Bounds.Size;
    public int FrameCount { get; private set; }
    public int AtlasIndex { get; private set; }
    public int BoneIndex { get; private set; }
    public float PixelsPerUnit { get; private set; } = 64.0f;
    public float PixelsPerUnitInv { get; private set; }
    public float FrameRate { get; private set; } = 12.0f;
    public TextureFilter TextureFilter { get; set; } = TextureFilter.Point;
    public SpriteMesh[] Meshes { get; private set; } = [];
    public SpriteFrameInfo[] FrameTable { get; private set; } = [];
    public EdgeInsets Edges { get; private set; } = EdgeInsets.Zero;
    public ushort SliceMask { get; private set; }
    public bool IsSliced => SliceMask != 0;
    public Rect UV => Meshes.Length > 0 ? Meshes[0].UV : Rect.Zero;
    public ushort Order => Meshes.Length > 0 ? (ushort)Meshes[0].SortOrder : (ushort)0;

    private Sprite(string name) : base(AssetType.Sprite, name) { }

    internal static Sprite Create(
        string name,
        RectInt bounds,
        float pixelsPerUnit,
        TextureFilter filter,
        int boneIndex,
        SpriteMesh[] meshes,
        SpriteFrameInfo[] frameTable,
        float frameRate = 12.0f,
        EdgeInsets edges = default,
        ushort sliceMask = 0)
    {
        return new Sprite(name)
        {
            Bounds = bounds,
            FrameCount = frameTable.Length,
            AtlasIndex = 0,
            PixelsPerUnit = pixelsPerUnit,
            PixelsPerUnitInv = 1.0f / pixelsPerUnit,
            FrameRate = frameRate,
            TextureFilter = filter,
            BoneIndex = boneIndex,
            Meshes = meshes,
            FrameTable = frameTable,
            Edges = edges,
            SliceMask = sliceMask,
        };
    }

    private static Asset Load(Stream stream, string name)
    {
        // Read version from asset header (sig:4 + type:4 + version:2 + flags:2 = 12 bytes)
        var savedPos = stream.Position;
        stream.Position = 8;
        var version = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true).ReadUInt16();
        stream.Position = savedPos;

        var sprite = new Sprite(name);
        var reader = new BinaryReader(stream);

        var frameCount = reader.ReadUInt16();
        var atlasIndex = reader.ReadUInt16();
        var l = reader.ReadInt16();
        var t = reader.ReadInt16();
        var r = reader.ReadInt16();
        var b = reader.ReadInt16();
        var ppu = reader.ReadSingle();
        var filter = (TextureFilter)reader.ReadByte();
        var boneIndex = reader.ReadInt16();
        var meshCount = reader.ReadUInt16();
        var frameRate = reader.ReadSingle();

        // 9-slice data (version 10+)
        var edges = EdgeInsets.Zero;
        ushort sliceMask = 0;
        if (version >= 10)
        {
            var et = reader.ReadInt16();
            var el = reader.ReadInt16();
            var eb = reader.ReadInt16();
            var er = reader.ReadInt16();
            edges = new EdgeInsets(et, el, eb, er);
            sliceMask = reader.ReadUInt16();
        }

        var meshes = new SpriteMesh[meshCount];
        for (int i = 0; i < meshCount; i++)
        {
            var ul = reader.ReadSingle();
            var ut = reader.ReadSingle();
            var ur = reader.ReadSingle();
            var ub = reader.ReadSingle();
            var sortOrder = reader.ReadInt16();
            var meshBoneIndex = reader.ReadInt16();
            var offsetX = reader.ReadInt16();
            var offsetY = reader.ReadInt16();
            var sizeX = reader.ReadInt16();
            var sizeY = reader.ReadInt16();

            meshes[i] = new SpriteMesh(
                Rect.FromMinMax(ul, ut, ur, ub),
                sortOrder,
                meshBoneIndex,
                new Vector2Int(offsetX, offsetY),
                new Vector2Int(sizeX, sizeY));
        }

        var frameTable = new SpriteFrameInfo[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            var meshStart = reader.ReadUInt16();
            var count = reader.ReadUInt16();
            frameTable[i] = new SpriteFrameInfo(meshStart, count);
        }

        sprite.Bounds = RectInt.FromMinMax(l, t, r, b);
        sprite.FrameCount = frameCount;
        sprite.AtlasIndex = atlasIndex;
        sprite.BoneIndex = boneIndex;
        sprite.PixelsPerUnit = ppu;
        sprite.PixelsPerUnitInv = 1.0f / ppu;
        sprite.FrameRate = frameRate;
        sprite.TextureFilter = filter;
        sprite.Meshes = meshes;
        sprite.FrameTable = frameTable;
        sprite.Edges = edges;
        sprite.SliceMask = sliceMask;

        return sprite;
    }

    public static ushort CalculateSliceMask(RectInt bounds, EdgeInsets edges)
    {
        if (edges.IsZero)
            return 0;

        var centerW = bounds.Width - edges.L - edges.R;
        var centerH = bounds.Height - edges.T - edges.B;

        bool[] colActive = [edges.L > 0, centerW > 0, edges.R > 0];
        bool[] rowActive = [edges.T > 0, centerH > 0, edges.B > 0];

        ushort mask = 0;
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                if (rowActive[row] && colActive[col])
                    mask |= (ushort)(1 << (row * 3 + col));

        return mask;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Sprite, "Sprite", typeof(Sprite), Load, Version));
    }
}
