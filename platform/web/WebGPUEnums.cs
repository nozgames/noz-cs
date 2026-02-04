//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Platform.Web;

/// <summary>
/// WebGPU buffer usage flags (matches GPUBufferUsage in JavaScript)
/// </summary>
[Flags]
public enum WebGPUBufferUsage
{
    MapRead = 0x0001,
    MapWrite = 0x0002,
    CopySrc = 0x0004,
    CopyDst = 0x0008,
    Index = 0x0010,
    Vertex = 0x0020,
    Uniform = 0x0040,
    Storage = 0x0080,
    Indirect = 0x0100,
    QueryResolve = 0x0200
}

/// <summary>
/// WebGPU texture usage flags (matches GPUTextureUsage in JavaScript)
/// </summary>
[Flags]
public enum WebGPUTextureUsage
{
    CopySrc = 0x01,
    CopyDst = 0x02,
    TextureBinding = 0x04,
    StorageBinding = 0x08,
    RenderAttachment = 0x10
}

/// <summary>
/// WebGPU shader stages
/// </summary>
[Flags]
public enum WebGPUShaderStage
{
    Vertex = 0x1,
    Fragment = 0x2,
    Compute = 0x4
}

/// <summary>
/// WebGPU texture formats (as strings for JS interop)
/// </summary>
public static class WebGPUTextureFormat
{
    public const string RGBA8 = "rgba8";
    public const string R8 = "r8";
    public const string RG8 = "rg8";
    public const string RGBA32F = "rgba32f";
    public const string BGRA8 = "bgra8";
}

/// <summary>
/// WebGPU blend mode names (for JS interop)
/// </summary>
public static class WebGPUBlendMode
{
    public const string None = "none";
    public const string Alpha = "alpha";
    public const string Additive = "additive";
    public const string Multiply = "multiply";
    public const string Premultiplied = "premultiplied";
}

/// <summary>
/// Binding types for bind group layout entries
/// </summary>
public enum WebGPUBindingType
{
    UniformBuffer,
    Texture2D,
    Texture2DArray,
    Texture2DUnfilterable,
    Sampler
}
