//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace NoZ;

[StructLayout(LayoutKind.Sequential)]
public struct BoneTransform
{
    public Vector2 Position;
    public float Rotation;
    public Vector2 Scale;

    public static readonly BoneTransform Identity = new()
    {
        Position = Vector2.Zero,
        Rotation = 0f,
        Scale = Vector2.One
    };
}

public struct Bone
{
    public string Name;
    public int Index;
    public int ParentIndex;
    public BoneTransform Transform;
    public float Radius;
}

public struct SkeletonState
{
    public string Name;
    public int Index;
    public int InitialValue;
}

public class Skeleton : Asset
{
    public const int MaxBones = 64;
    public const int MaxStates = 16;

    public int BoneCount { get; private set; }
    public Bone[] Bones { get; private set; } = [];
    public NativeArray<Matrix3x2> BindPoses { get; private set; }

    public int StateCount { get; private set; }
    public SkeletonState[] States { get; private set; } = [];

    private Skeleton(string name) : base(AssetType.Skeleton, name)
    {
    }

    public Skeleton() : base(AssetType.Skeleton) { }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Skeleton, "Skeleton", typeof(Skeleton), Load));
    }

    protected override void Load(BinaryReader reader)
    {
        var boneCount = reader.ReadByte();
        BoneCount = boneCount;
        Bones = new Bone[boneCount];
        BindPoses = new NativeArray<Matrix3x2>(boneCount, boneCount);

        for (var i = 0; i < boneCount; i++)
        {
            ref var bone = ref Bones[i];
            bone.Name = reader.ReadString();
            bone.Index = i;
            bone.ParentIndex = reader.ReadSByte();
            bone.Transform.Position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            bone.Transform.Rotation = reader.ReadSingle();
            bone.Transform.Scale = new Vector2(reader.ReadSingle(), reader.ReadSingle());

            BindPoses[i] = new Matrix3x2(
                reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadSingle(), reader.ReadSingle()
            );

            if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
            {
                bone.Radius = reader.ReadSingle();
            }
        }

        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var stateCount = reader.ReadByte();
            StateCount = stateCount;
            States = new SkeletonState[stateCount];
            for (var i = 0; i < stateCount; i++)
            {
                States[i].Name = reader.ReadString();
                States[i].Index = i;
                States[i].InitialValue = reader.ReadInt32();
            }
        }
    }

    private static Skeleton? Load(Stream stream, string name)
    {
        var skeleton = new Skeleton(name);
        var reader = new BinaryReader(stream);
        skeleton.Load(reader);
        return skeleton;
    }

    public static Skeleton Create(string name, Bone[] bones)
    {
        return new Skeleton(name)
        {
            BoneCount = bones.Length,
            Bones = bones
        };
    }

    public int GetBoneIndex(string name)
    {
        for (var i = 0; i < BoneCount; i++)
            if (Bones[i].Name == name)
                return i;

        return 0;
    }

    public ref Bone GetBone(int boneIndex) =>
        ref Bones[boneIndex];

    public int GetStateIndex(string name)
    {
        for (var i = 0; i < StateCount; i++)
            if (States[i].Name == name)
                return i;

        return -1;
    }

    public ref SkeletonState GetState(int stateIndex) =>
        ref States[stateIndex];
}
