namespace NoZ;

/// <summary>Channels supplied by the source, unioned across the mesh's primitives.
/// Unknown is used for older compiled meshes which did not record this metadata.</summary>
[Flags]
public enum MeshChannels
{
    Unknown = 0,
    Position = 1 << 0,
    Normal = 1 << 1,
    Tangent = 1 << 2,
    UV1 = 1 << 3,
    UV2 = 1 << 4,
    UV3 = 1 << 5,
    Color = 1 << 6,
}
