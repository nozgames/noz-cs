//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using NoZ.Platform;

namespace NoZ.Platform.Web;

/// <summary>
/// IGraphicsDriver implementation for browser WebGPU using JSImport interop
/// </summary>
public class WebGraphicsDriver : IGraphicsDriver
{
    private GraphicsDriverConfig _config = null!;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private string _surfaceFormat = "";

    // Resource tracking (ID-based, matching JS side)
    private int _nextMeshId = 1;
    private int _nextBufferId = 1;
    private int _nextTextureId = 2; // 1 reserved for white texture
    private int _nextShaderId = 1;

    private readonly Dictionary<nuint, MeshInfo> _meshes = new();
    private readonly Dictionary<nuint, BufferInfo> _buffers = new();
    private readonly Dictionary<nuint, TextureInfo> _textures = new();
    private readonly Dictionary<nuint, ShaderInfo> _shaders = new();

    // Cached state
    private CachedState _state;

    // Per-name uniform data storage
    private readonly Dictionary<string, byte[]> _uniformData = new();

    // Globals buffer management
    private const int MaxGlobalsBuffers = 64;
    private const int GlobalsBufferSize = 80; // mat4 (64) + float (4) + padding (12)
    private readonly int[] _globalsBuffers = new int[MaxGlobalsBuffers];
    private int _globalsBufferCount;
    private int _currentGlobalsIndex = -1;

    // Fullscreen quad mesh for compositing
    private nuint _fullscreenQuadMesh;

    // Offscreen rendering state
    private Vector2Int _offscreenSize;
    private int _msaaSamples = 1;

    public string ShaderExtension => "";

    private struct CachedState
    {
        public nuint BoundShader;
        public BlendMode BlendMode;
        public TextureFilter TextureFilter;
        public nuint BoundMesh;
        public nuint[] BoundTextures;
        public TextureFilter[] TextureFilters;
        public bool PipelineDirty;
        public bool BindGroupDirty;
        public RectInt Viewport;
        public bool ScissorEnabled;
        public RectInt Scissor;
        public int CurrentPassSampleCount;
        public int CurrentPipelineId;
        public int CurrentBindGroupId;
    }

    private struct MeshInfo
    {
        public int JsMeshId;
        public int Stride;
        public int MaxVertices;
        public int MaxIndices;
        public VertexFormatDescriptor Descriptor;
    }

    private struct BufferInfo
    {
        public int JsBufferId;
        public int SizeInBytes;
        public BufferUsage Usage;
    }

    private struct TextureInfo
    {
        public int JsTextureId;
        public int Width;
        public int Height;
        public int Layers;
        public string Format;
        public bool IsArray;
    }

    private struct ShaderInfo
    {
        public string Name;
        public int VertexModuleId;
        public int FragmentModuleId;
        public int BindGroupLayoutId;
        public int PipelineLayoutId;
        public Dictionary<PsoKey, int> PsoCache; // Maps PSO key to JS pipeline ID
        public int BindGroupEntryCount;
        public List<ShaderBinding> Bindings;
        public List<TextureSlotInfo> TextureSlots;
        public Dictionary<string, uint> UniformBindings;
        public Dictionary<string, int> UniformBuffers; // Per-shader uniform buffers (name -> JS buffer ID)
    }

    private struct TextureSlotInfo
    {
        public uint TextureBinding;
        public uint SamplerBinding;
        public bool IsUnfilterable;
    }

    private struct PsoKey : IEquatable<PsoKey>
    {
        public nuint ShaderHandle;
        public BlendMode BlendMode;
        public int VertexStride;
        public int MsaaSamples;

        public bool Equals(PsoKey other) =>
            ShaderHandle == other.ShaderHandle &&
            BlendMode == other.BlendMode &&
            VertexStride == other.VertexStride &&
            MsaaSamples == other.MsaaSamples;

        public override bool Equals(object? obj) => obj is PsoKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ShaderHandle, BlendMode, VertexStride, MsaaSamples);
    }

    public void Init(GraphicsDriverConfig config)
    {
        _config = config;
        // Actual initialization happens in InitAsync
    }

    /// <summary>
    /// Async initialization - must be called after Init()
    /// </summary>
    public async Task InitAsync()
    {
        // Import the JS module first before calling any JSImport functions
        // Use absolute path from web root (not relative, which resolves from _framework/)
        await JSHost.ImportAsync("noz-webgpu", "/js/noz/noz-webgpu.js");

        var result = await WebGPUInterop.InitAsync("#canvas");

        _surfaceWidth = result.GetPropertyAsInt32("width");
        _surfaceHeight = result.GetPropertyAsInt32("height");
        _surfaceFormat = result.GetPropertyAsString("format") ?? "bgra8unorm";

        _state = new CachedState
        {
            BoundTextures = new nuint[8],
            TextureFilters = new TextureFilter[8]
        };

        CreateFullscreenQuad();

        Log.Info($"WebGraphicsDriver initialized: {_surfaceFormat}, {_surfaceWidth}x{_surfaceHeight}");
    }

    public void Shutdown()
    {
        WebGPUInterop.Shutdown();

        _meshes.Clear();
        _buffers.Clear();
        _textures.Clear();
        _shaders.Clear();
    }

    // ============================================================================
    // Frame Management
    // ============================================================================

    public bool BeginFrame()
    {
        if (!WebGPUInterop.BeginFrame())
            return false;

        // Check for resize
        var windowSize = _config.Platform.WindowSize;
        var newWidth = (int)windowSize.X;
        var newHeight = (int)windowSize.Y;
        if (newWidth != _surfaceWidth || newHeight != _surfaceHeight)
        {
            _surfaceWidth = newWidth;
            _surfaceHeight = newHeight;
        }

        _state.CurrentPipelineId = 0;
        _state.CurrentBindGroupId = 0;

        return true;
    }

    public void EndFrame()
    {
        WebGPUInterop.EndFrame();
    }

    public void Clear(Color color)
    {
        // Clear is handled in BeginScenePass
    }

    // ============================================================================
    // Viewport / Scissor
    // ============================================================================

    public void SetViewport(in RectInt viewport)
    {
        var clampedViewport = viewport;
        clampedViewport.Width = Math.Min(viewport.Width, _surfaceWidth - viewport.X);
        clampedViewport.Height = Math.Min(viewport.Height, _surfaceHeight - viewport.Y);
        if (clampedViewport.Width <= 0 || clampedViewport.Height <= 0)
            return;

        if (_state.Viewport == clampedViewport)
            return;

        _state.Viewport = clampedViewport;
        WebGPUInterop.SetViewport(clampedViewport.X, clampedViewport.Y, clampedViewport.Width, clampedViewport.Height, 0, 1);
    }

    public void SetScissor(in RectInt scissor)
    {
        var clampedScissor = scissor;
        clampedScissor.X = Math.Max(0, scissor.X);
        clampedScissor.Y = Math.Max(0, scissor.Y);
        clampedScissor.Width = Math.Min(scissor.Width, _surfaceWidth - scissor.X);
        clampedScissor.Height = Math.Min(scissor.Height, _surfaceHeight - scissor.Y);
        if (clampedScissor.Width <= 0 || clampedScissor.Height <= 0)
            return;

        _state.ScissorEnabled = true;
        _state.Scissor = clampedScissor;

        WebGPUInterop.SetScissorRect(clampedScissor.X, clampedScissor.Y, clampedScissor.Width, clampedScissor.Height);
    }

    public void ClearScissor()
    {
        _state.ScissorEnabled = false;
        WebGPUInterop.SetScissorRect(0, 0, _surfaceWidth, _surfaceHeight);
    }

    // ============================================================================
    // Mesh Management
    // ============================================================================

    public nuint CreateMesh<T>(int maxVertices, int maxIndices, BufferUsage usage, string name = "") where T : IVertex
    {
        var descriptor = T.GetFormatDescriptor();

        var jsMeshId = WebGPUInterop.CreateMesh(maxVertices, maxIndices, descriptor.Stride, name);

        var handle = (nuint)_nextMeshId++;
        _meshes[handle] = new MeshInfo
        {
            JsMeshId = jsMeshId,
            Stride = descriptor.Stride,
            MaxVertices = maxVertices,
            MaxIndices = maxIndices,
            Descriptor = descriptor
        };

        return handle;
    }

    public void DestroyMesh(nuint handle)
    {
        if (_meshes.TryGetValue(handle, out var mesh))
        {
            WebGPUInterop.DestroyMesh(mesh.JsMeshId);
            _meshes.Remove(handle);
        }
    }

    public void BindMesh(nuint handle)
    {
        if (_state.BoundMesh == handle)
            return;

        _state.BoundMesh = handle;
        _state.PipelineDirty = true;
    }

    public void UpdateMesh(nuint handle, ReadOnlySpan<byte> vertexData, ReadOnlySpan<ushort> indexData)
    {
        if (!_meshes.TryGetValue(handle, out var mesh))
        {
            Log.Error($"Mesh {handle} not found");
            return;
        }

        var vertexArray = vertexData.Length > 0 ? vertexData.ToArray() : Array.Empty<byte>();
        var indexBytes = indexData.Length > 0 ? MemoryMarshal.AsBytes(indexData).ToArray() : Array.Empty<byte>();

        WebGPUInterop.UpdateMesh(mesh.JsMeshId, new ArraySegment<byte>(vertexArray), new ArraySegment<byte>(indexBytes));
    }

    // ============================================================================
    // Buffer Management
    // ============================================================================

    public nuint CreateUniformBuffer(int sizeInBytes, BufferUsage usage, string name = "")
    {
        var jsBufferId = WebGPUInterop.CreateBuffer(sizeInBytes, (int)(WebGPUBufferUsage.Uniform | WebGPUBufferUsage.CopyDst), name);

        var handle = (nuint)_nextBufferId++;
        _buffers[handle] = new BufferInfo
        {
            JsBufferId = jsBufferId,
            SizeInBytes = sizeInBytes,
            Usage = usage
        };

        return handle;
    }

    public void DestroyBuffer(nuint handle)
    {
        if (_buffers.TryGetValue(handle, out var buffer))
        {
            WebGPUInterop.DestroyBuffer(buffer.JsBufferId);
            _buffers.Remove(handle);
        }
    }

    public void UpdateUniformBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data)
    {
        if (!_buffers.TryGetValue(buffer, out var bufferInfo))
        {
            Log.Error($"Buffer {buffer} not found");
            return;
        }

        WebGPUInterop.WriteBuffer(bufferInfo.JsBufferId, offsetBytes, new ArraySegment<byte>(data.ToArray()));
    }

    public void BindUniformBuffer(nuint buffer, int slot)
    {
        // Uniform buffer binding is handled through bind groups
        _state.BindGroupDirty = true;
    }

    // ============================================================================
    // Texture Management
    // ============================================================================

    private static string MapTextureFormat(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.RGBA8 => WebGPUTextureFormat.RGBA8,
            TextureFormat.RGB8 => WebGPUTextureFormat.RGBA8, // WebGPU doesn't support RGB8
            TextureFormat.R8 => WebGPUTextureFormat.R8,
            TextureFormat.RG8 => WebGPUTextureFormat.RGBA8, // Fallback
            TextureFormat.RGBA32F => WebGPUTextureFormat.RGBA32F,
            _ => WebGPUTextureFormat.RGBA8
        };
    }

    private static int GetBytesPerPixel(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.RGBA8 => 4,
            TextureFormat.RGB8 => 3,
            TextureFormat.R8 => 1,
            TextureFormat.RG8 => 2,
            TextureFormat.RGBA32F => 16,
            _ => 4
        };
    }

    public nuint CreateTexture(int width, int height, ReadOnlySpan<byte> data, TextureFormat format = TextureFormat.RGBA8, TextureFilter filter = TextureFilter.Linear, string? name = null)
    {
        var gpuFormat = MapTextureFormat(format);
        var usage = (int)(WebGPUTextureUsage.TextureBinding | WebGPUTextureUsage.CopyDst);

        var jsTextureId = WebGPUInterop.CreateTexture(width, height, gpuFormat, usage, name);

        if (data.Length > 0)
        {
            var bytesPerPixel = GetBytesPerPixel(format);
            WebGPUInterop.WriteTexture(jsTextureId, new ArraySegment<byte>(data.ToArray()), width, height, width * bytesPerPixel, 0);
        }

        var handle = (nuint)_nextTextureId++;
        _textures[handle] = new TextureInfo
        {
            JsTextureId = jsTextureId,
            Width = width,
            Height = height,
            Layers = 1,
            Format = gpuFormat,
            IsArray = false
        };

        return handle;
    }

    public void UpdateTexture(nuint handle, in Vector2Int size, ReadOnlySpan<byte> data)
    {
        if (!_textures.TryGetValue(handle, out var textureInfo))
        {
            Log.Error($"Texture {handle} not found");
            return;
        }

        var bytesPerPixel = textureInfo.Format switch
        {
            "rgba8" => 4,
            "r8" => 1,
            "rgba32f" => 16,
            _ => 4
        };

        WebGPUInterop.WriteTexture(textureInfo.JsTextureId, new ArraySegment<byte>(data.ToArray()), size.X, size.Y, size.X * bytesPerPixel, 0);
    }

    public void UpdateTextureRegion(nuint handle, in RectInt region, ReadOnlySpan<byte> data, int srcWidth = -1)
    {
        if (!_textures.TryGetValue(handle, out var textureInfo))
        {
            Log.Error($"Texture {handle} not found");
            return;
        }

        var bytesPerPixel = textureInfo.Format switch
        {
            "rgba8" => 4,
            "r8" => 1,
            "rgba32f" => 16,
            _ => 4
        };

        var rowWidth = srcWidth < 0 ? region.Width : srcWidth;
        WebGPUInterop.WriteTextureRegion(textureInfo.JsTextureId, new ArraySegment<byte>(data.ToArray()), region.X, region.Y, region.Width, region.Height, rowWidth * bytesPerPixel);
    }

    public void DestroyTexture(nuint handle)
    {
        if (_textures.TryGetValue(handle, out var texture))
        {
            WebGPUInterop.DestroyTexture(texture.JsTextureId);
            _textures.Remove(handle);
        }
    }

    public void BindTexture(nuint handle, int slot, TextureFilter filter = TextureFilter.Point)
    {
        if (slot < 0 || slot >= 8)
        {
            Log.Error($"Texture slot {slot} out of range (0-7)!");
            return;
        }

        if (_state.BoundTextures[slot] == handle && _state.TextureFilters[slot] == filter)
            return;

        _state.BoundTextures[slot] = handle;
        _state.TextureFilters[slot] = filter;
        _state.BindGroupDirty = true;
    }

    // ============================================================================
    // Texture Array Management
    // ============================================================================

    public nuint CreateTextureArray(int width, int height, int layers)
    {
        var jsTextureId = WebGPUInterop.CreateTextureArray(width, height, layers, WebGPUTextureFormat.RGBA8, null);

        var handle = (nuint)_nextTextureId++;
        _textures[handle] = new TextureInfo
        {
            JsTextureId = jsTextureId,
            Width = width,
            Height = height,
            Layers = layers,
            Format = WebGPUTextureFormat.RGBA8,
            IsArray = true
        };

        return handle;
    }

    public nuint CreateTextureArray(int width, int height, byte[][] layerData, TextureFormat format, TextureFilter filter, string? name = null)
    {
        var gpuFormat = MapTextureFormat(format);
        var layers = layerData.Length;

        var jsTextureId = WebGPUInterop.CreateTextureArray(width, height, layers, gpuFormat, name);

        var bytesPerPixel = GetBytesPerPixel(format);
        for (int i = 0; i < layers; i++)
        {
            WebGPUInterop.WriteTexture(jsTextureId, new ArraySegment<byte>(layerData[i]), width, height, width * bytesPerPixel, i);
        }

        var handle = (nuint)_nextTextureId++;
        _textures[handle] = new TextureInfo
        {
            JsTextureId = jsTextureId,
            Width = width,
            Height = height,
            Layers = layers,
            Format = gpuFormat,
            IsArray = true
        };

        return handle;
    }

    public void UpdateTextureLayer(nuint handle, int layer, ReadOnlySpan<byte> data)
    {
        if (!_textures.TryGetValue(handle, out var textureInfo))
        {
            Log.Error($"Texture {handle} not found");
            return;
        }

        var bytesPerPixel = textureInfo.Format switch
        {
            "rgba8" => 4,
            "r8" => 1,
            "rgba32f" => 16,
            _ => 4
        };

        WebGPUInterop.WriteTexture(textureInfo.JsTextureId, new ArraySegment<byte>(data.ToArray()), textureInfo.Width, textureInfo.Height, textureInfo.Width * bytesPerPixel, layer);
    }

    // ============================================================================
    // Shader Management
    // ============================================================================

    public nuint CreateShader(string name, string vertexSource, string fragmentSource, List<ShaderBinding> bindings)
    {
        var vertexModuleId = WebGPUInterop.CreateShaderModule(vertexSource, $"{name}_vertex");
        var fragmentModuleId = WebGPUInterop.CreateShaderModule(fragmentSource, $"{name}_fragment");

        // Build texture slots and uniform bindings from metadata
        var textureSlots = new List<TextureSlotInfo>();
        var uniformBindings = new Dictionary<string, uint>();

        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];

            if (binding.Type == ShaderBindingType.UniformBuffer)
                uniformBindings[binding.Name] = binding.Binding;

            if (binding.Type == ShaderBindingType.Texture2D || binding.Type == ShaderBindingType.Texture2DArray)
            {
                bool hasSampler = i + 1 < bindings.Count && bindings[i + 1].Type == ShaderBindingType.Sampler;
                bool isUnfilterable = !hasSampler;

                // Update binding type to unfilterable if no sampler follows (browser WebGPU requires this)
                if (isUnfilterable && binding.Type == ShaderBindingType.Texture2D)
                {
                    bindings[i] = binding with { Type = ShaderBindingType.Texture2DUnfilterable };
                }

                textureSlots.Add(new TextureSlotInfo
                {
                    TextureBinding = binding.Binding,
                    SamplerBinding = hasSampler ? binding.Binding + 1 : 0,
                    IsUnfilterable = isUnfilterable
                });
            }
        }

        // Create bind group layout
        var layoutEntries = CreateBindGroupLayoutEntries(bindings);
        var bindGroupLayoutId = WebGPUInterop.CreateBindGroupLayout(layoutEntries, $"{name}_layout");

        // Create pipeline layout
        var pipelineLayoutId = WebGPUInterop.CreatePipelineLayout(new[] { bindGroupLayoutId }, $"{name}_pipeline_layout");

        var handle = (nuint)_nextShaderId++;
        _shaders[handle] = new ShaderInfo
        {
            Name = name,
            VertexModuleId = vertexModuleId,
            FragmentModuleId = fragmentModuleId,
            BindGroupLayoutId = bindGroupLayoutId,
            PipelineLayoutId = pipelineLayoutId,
            PsoCache = new Dictionary<PsoKey, int>(),
            BindGroupEntryCount = bindings.Count,
            Bindings = bindings,
            TextureSlots = textureSlots,
            UniformBindings = uniformBindings,
            UniformBuffers = new Dictionary<string, int>()
        };

        return handle;
    }

    private JSObject[] CreateBindGroupLayoutEntries(List<ShaderBinding> bindings)
    {
        var entries = new JSObject[bindings.Count];

        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            var entry = JSObjectHelper.CreateBindGroupLayoutEntry(binding);
            entries[i] = entry;
        }

        return entries;
    }

    public void DestroyShader(nuint handle)
    {
        if (_shaders.TryGetValue(handle, out var shader))
        {
            // Destroy cached pipelines
            foreach (var pipelineId in shader.PsoCache.Values)
            {
                WebGPUInterop.DestroyRenderPipeline(pipelineId);
            }

            // Destroy per-shader uniform buffers
            foreach (var bufferId in shader.UniformBuffers.Values)
            {
                WebGPUInterop.DestroyBuffer(bufferId);
            }

            WebGPUInterop.DestroyShaderModule(shader.VertexModuleId);
            WebGPUInterop.DestroyShaderModule(shader.FragmentModuleId);

            _shaders.Remove(handle);
        }
    }

    public void BindShader(nuint handle)
    {
        if (_state.BoundShader == handle)
            return;

        _state.BoundShader = handle;
        _state.PipelineDirty = true;
        _state.BindGroupDirty = true;
    }

    // ============================================================================
    // Render State
    // ============================================================================

    public void SetBlendMode(BlendMode mode)
    {
        if (_state.BlendMode == mode)
            return;

        _state.BlendMode = mode;
        _state.PipelineDirty = true;
    }

    public void SetTextureFilter(TextureFilter filter)
    {
        if (_state.TextureFilter == filter)
            return;

        _state.TextureFilter = filter;
        _state.BindGroupDirty = true;
    }

    public void SetUniform(string name, ReadOnlySpan<byte> data)
    {
        if (!_uniformData.TryGetValue(name, out var existing) || existing.Length != data.Length)
            _uniformData[name] = new byte[data.Length];

        data.CopyTo(_uniformData[name]);
        _state.BindGroupDirty = true;
    }

    // ============================================================================
    // Globals Management
    // ============================================================================

    public void SetGlobalsCount(int count)
    {
        while (_globalsBufferCount < count)
        {
            var bufferId = WebGPUInterop.CreateBuffer(GlobalsBufferSize, (int)(WebGPUBufferUsage.Uniform | WebGPUBufferUsage.CopyDst), $"globals_{_globalsBufferCount}");
            _globalsBuffers[_globalsBufferCount] = bufferId;
            _globalsBufferCount++;
        }
    }

    public void SetGlobals(int index, ReadOnlySpan<byte> data)
    {
        if (index < 0 || index >= _globalsBufferCount)
            return;

        WebGPUInterop.WriteBuffer(_globalsBuffers[index], 0, new ArraySegment<byte>(data.ToArray()));
    }

    public void BindGlobals(int index)
    {
        if (_currentGlobalsIndex == index)
            return;

        _currentGlobalsIndex = index;
        _state.BindGroupDirty = true;
    }

    // ============================================================================
    // Drawing
    // ============================================================================

    public void DrawElements(int firstIndex, int indexCount, int baseVertex = 0)
    {
        // Update pipeline if needed
        if (_state.PipelineDirty)
        {
            var pipelineId = GetOrCreatePipeline(_state.BoundShader, _state.BlendMode, _meshes[_state.BoundMesh].Stride);
            if (pipelineId > 0)
            {
                WebGPUInterop.SetPipeline(pipelineId);
                _state.CurrentPipelineId = pipelineId;
            }
            _state.PipelineDirty = false;
        }

        // Update bind group if needed
        if (_state.BindGroupDirty)
        {
            var bindGroupId = CreateBindGroup();
            if (bindGroupId > 0)
            {
                WebGPUInterop.SetBindGroup(0, bindGroupId);
                _state.CurrentBindGroupId = bindGroupId;
            }
            _state.BindGroupDirty = false;
        }

        // Bind mesh buffers
        var mesh = _meshes[_state.BoundMesh];
        WebGPUInterop.SetVertexBuffer(0, mesh.JsMeshId);
        WebGPUInterop.SetIndexBuffer(mesh.JsMeshId);

        // Apply scissor (no Y flip needed - handled by projection matrix)
        if (_state.ScissorEnabled)
        {
            WebGPUInterop.SetScissorRect(_state.Scissor.X, _state.Scissor.Y, _state.Scissor.Width, _state.Scissor.Height);
        }
        else
        {
            WebGPUInterop.SetScissorRect(0, 0, _surfaceWidth, _surfaceHeight);
        }

        // Draw
        WebGPUInterop.DrawIndexed(indexCount, 1, firstIndex, baseVertex, 0);
    }

    private int GetOrCreatePipeline(nuint shaderHandle, BlendMode blendMode, int vertexStride)
    {
        if (!_shaders.TryGetValue(shaderHandle, out var shader))
        {
            Log.Error($"Shader {shaderHandle} not found");
            return 0;
        }

        var key = new PsoKey
        {
            ShaderHandle = shaderHandle,
            BlendMode = blendMode,
            VertexStride = vertexStride,
            MsaaSamples = _state.CurrentPassSampleCount
        };

        if (shader.PsoCache.TryGetValue(key, out var pipelineId))
            return pipelineId;

        // Create new pipeline
        var mesh = _meshes[_state.BoundMesh];
        var descriptor = JSObjectHelper.CreateRenderPipelineDescriptor(
            shader.VertexModuleId,
            shader.FragmentModuleId,
            shader.PipelineLayoutId,
            mesh.Descriptor,
            blendMode,
            _state.CurrentPassSampleCount,
            _surfaceFormat,
            $"{shader.Name}_{blendMode}_{vertexStride}b_{key.MsaaSamples}x"
        );

        pipelineId = WebGPUInterop.CreateRenderPipeline(descriptor);
        shader.PsoCache[key] = pipelineId;

        return pipelineId;
    }

    private int CreateBindGroup()
    {
        if (!_shaders.TryGetValue(_state.BoundShader, out var shader))
        {
            Log.Error($"Shader {_state.BoundShader} not found");
            return 0;
        }

        var bindings = shader.Bindings;
        if (bindings == null || bindings.Count == 0)
        {
            Log.Error("Shader has no binding metadata!");
            return 0;
        }

        // Use JSON serialization instead of JSObject array for reliable marshalling
        var entries = new List<BindGroupEntryData>();

        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];

            switch (binding.Type)
            {
                case ShaderBindingType.UniformBuffer:
                {
                    int bufferId;
                    int bufferSize;

                    if (binding.Name == "globals")
                    {
                        if (_currentGlobalsIndex < 0 || _currentGlobalsIndex >= _globalsBufferCount)
                        {
                            Log.Error($"Globals index {_currentGlobalsIndex} out of range!");
                            return 0;
                        }
                        bufferId = _globalsBuffers[_currentGlobalsIndex];
                        bufferSize = GlobalsBufferSize;
                    }
                    else
                    {
                        if (!_uniformData.TryGetValue(binding.Name, out var uniformData))
                        {
                            Log.Error($"Uniform '{binding.Name}' not set!");
                            return 0;
                        }

                        if (!shader.UniformBuffers.TryGetValue(binding.Name, out bufferId) || bufferId == 0)
                        {
                            bufferId = WebGPUInterop.CreateBuffer(uniformData.Length, (int)(WebGPUBufferUsage.Uniform | WebGPUBufferUsage.CopyDst), $"{shader.Name}_{binding.Name}");
                            shader.UniformBuffers[binding.Name] = bufferId;
                        }

                        WebGPUInterop.WriteBuffer(bufferId, 0, new ArraySegment<byte>(uniformData));
                        bufferSize = uniformData.Length;
                    }

                    entries.Add(new BindGroupEntryData { type = "buffer", binding = (int)binding.Binding, bufferId = bufferId, size = bufferSize });
                    break;
                }

                case ShaderBindingType.Texture2D:
                case ShaderBindingType.Texture2DArray:
                case ShaderBindingType.Texture2DUnfilterable:
                {
                    int textureSlot = GetTextureSlotForBinding(binding.Binding, shader);
                    nuint textureHandle = textureSlot >= 0 ? _state.BoundTextures[textureSlot] : 0;

                    if (textureHandle == 0 || !_textures.TryGetValue(textureHandle, out var tex))
                    {
                        Log.Error($"Texture slot {textureSlot} (binding {binding.Binding}) not bound!");
                        return 0;
                    }

                    entries.Add(new BindGroupEntryData { type = "texture", binding = (int)binding.Binding, textureId = tex.JsTextureId });
                    break;
                }

                case ShaderBindingType.Sampler:
                {
                    int textureSlot = GetTextureSlotForBinding(binding.Binding, shader);
                    var slotFilter = textureSlot >= 0 ? _state.TextureFilters[textureSlot] : TextureFilter.Point;
                    bool useLinear = slotFilter == TextureFilter.Linear;

                    entries.Add(new BindGroupEntryData { type = "sampler", binding = (int)binding.Binding, useLinear = useLinear });
                    break;
                }
            }
        }

        var entriesJson = System.Text.Json.JsonSerializer.Serialize(entries);
        return WebGPUInterop.CreateBindGroupFromJson(shader.BindGroupLayoutId, entriesJson, null);
    }

    // Simple data class for JSON serialization of bind group entries
    private class BindGroupEntryData
    {
        public string type { get; set; } = "";
        public int binding { get; set; }
        public int bufferId { get; set; }
        public int size { get; set; }
        public int textureId { get; set; }
        public bool useLinear { get; set; }
    }

    private int GetTextureSlotForBinding(uint bindingNumber, ShaderInfo shader)
    {
        for (int i = 0; i < shader.TextureSlots.Count; i++)
        {
            var slot = shader.TextureSlots[i];
            if (slot.TextureBinding == bindingNumber || slot.SamplerBinding == bindingNumber)
                return i;
        }
        return -1;
    }

    // ============================================================================
    // Fences (stubs for web)
    // ============================================================================

    public nuint CreateFence() => 0;
    public void WaitFence(nuint fence) { }
    public void DeleteFence(nuint fence) { }

    // ============================================================================
    // Render Passes
    // ============================================================================

    public void ResizeOffscreenTarget(Vector2Int size, int msaaSamples)
    {
        if (_offscreenSize == size && _msaaSamples == msaaSamples)
            return;

        _offscreenSize = size;
        _msaaSamples = Math.Max(1, msaaSamples);

        WebGPUInterop.ResizeOffscreenTarget(size.X, size.Y, _msaaSamples);
    }

    public void BeginScenePass(Color clearColor)
    {
        _state.CurrentPassSampleCount = _msaaSamples;

        var colorAttachment = JSObjectHelper.CreateColorAttachment(
            -2, // offscreen MSAA texture
            _msaaSamples > 1 ? -3 : 0, // resolve texture if MSAA
            "clear",
            _msaaSamples > 1 ? "discard" : "store",
            clearColor
        );

        WebGPUInterop.BeginRenderPass(new[] { colorAttachment }, null, "ScenePass");
        WebGPUInterop.SetViewport(0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        WebGPUInterop.SetScissorRect(0, 0, _surfaceWidth, _surfaceHeight);
    }

    public void EndScenePass()
    {
        WebGPUInterop.EndRenderPass();
    }

    public void Composite(nuint compositeShader)
    {
        _state.CurrentPassSampleCount = 1;

        var colorAttachment = JSObjectHelper.CreateColorAttachment(
            -1, // surface texture
            0, // no resolve
            "clear",
            "store",
            new Color(0, 0, 0, 1)
        );

        WebGPUInterop.BeginRenderPass(new[] { colorAttachment }, null, "Composite");
        WebGPUInterop.SetViewport(0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        WebGPUInterop.SetScissorRect(0, 0, _surfaceWidth, _surfaceHeight);

        BindShader(compositeShader);

        // Create composite bind group manually (texture + sampler from offscreen)
        var shader = _shaders[compositeShader];
        var entries = new[]
        {
            JSObjectHelper.CreateOffscreenTextureBindGroupEntry(0),
            JSObjectHelper.CreateSamplerBindGroupEntry(1, true) // linear sampler
        };
        var bindGroupId = WebGPUInterop.CreateBindGroup(shader.BindGroupLayoutId, entries, "composite_bind_group");

        // Get/create pipeline
        var pipelineId = GetOrCreateCompositeQuadPipeline(compositeShader);

        WebGPUInterop.SetPipeline(pipelineId);
        WebGPUInterop.SetBindGroup(0, bindGroupId);
        WebGPUInterop.DrawFullscreenQuad();

        WebGPUInterop.EndRenderPass();
    }

    public void BeginUIPass()
    {
        _state.CurrentPassSampleCount = 1;

        var colorAttachment = JSObjectHelper.CreateColorAttachment(
            -1, // surface texture
            0, // no resolve
            "load", // preserve existing content
            "store",
            new Color(0, 0, 0, 1)
        );

        WebGPUInterop.BeginRenderPass(new[] { colorAttachment }, null, "UIPass");
        WebGPUInterop.SetViewport(0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        WebGPUInterop.SetScissorRect(0, 0, _surfaceWidth, _surfaceHeight);

        _state.PipelineDirty = true;
        _state.BindGroupDirty = true;
    }

    public void EndUIPass()
    {
        WebGPUInterop.EndRenderPass();
    }

    // ============================================================================
    // Fullscreen Quad
    // ============================================================================

    private void CreateFullscreenQuad()
    {
        _fullscreenQuadMesh = CreateMesh<CompositeVertex>(4, 6, BufferUsage.Static, "fullscreen_quad");

        var vertices = new CompositeVertex[4];
        vertices[0] = new CompositeVertex { Position = new Vector2(-1, -1), UV = new Vector2(0, 0) };
        vertices[1] = new CompositeVertex { Position = new Vector2(1, -1), UV = new Vector2(1, 0) };
        vertices[2] = new CompositeVertex { Position = new Vector2(-1, 1), UV = new Vector2(0, 1) };
        vertices[3] = new CompositeVertex { Position = new Vector2(1, 1), UV = new Vector2(1, 1) };

        var indices = new ushort[] { 0, 1, 2, 1, 3, 2 };

        UpdateMesh(_fullscreenQuadMesh, MemoryMarshal.AsBytes(vertices.AsSpan()), indices.AsSpan());
    }

    private int GetOrCreateCompositeQuadPipeline(nuint shaderHandle)
    {
        if (!_shaders.TryGetValue(shaderHandle, out var shader))
        {
            Log.Error($"Composite shader {shaderHandle} not found");
            return 0;
        }

        var key = new PsoKey
        {
            ShaderHandle = shaderHandle,
            BlendMode = BlendMode.None,
            VertexStride = CompositeVertex.SizeInBytes,
            MsaaSamples = 1
        };

        if (shader.PsoCache.TryGetValue(key, out var pipelineId))
            return pipelineId;

        var descriptor = JSObjectHelper.CreateRenderPipelineDescriptor(
            shader.VertexModuleId,
            shader.FragmentModuleId,
            shader.PipelineLayoutId,
            CompositeVertex.GetFormatDescriptor(),
            BlendMode.None,
            1,
            _surfaceFormat,
            $"{shader.Name}_composite"
        );

        pipelineId = WebGPUInterop.CreateRenderPipeline(descriptor);
        shader.PsoCache[key] = pipelineId;

        return pipelineId;
    }
}

/// <summary>
/// Composite vertex for fullscreen quad
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CompositeVertex : IVertex
{
    public Vector2 Position;
    public Vector2 UV;

    public static readonly int SizeInBytes = Marshal.SizeOf(typeof(CompositeVertex));

    public static VertexFormatDescriptor GetFormatDescriptor() => new()
    {
        Stride = SizeInBytes,
        Attributes =
        [
            new VertexAttribute(0, 2, VertexAttribType.Float, (int)Marshal.OffsetOf<CompositeVertex>(nameof(Position))),
            new VertexAttribute(1, 2, VertexAttribType.Float, (int)Marshal.OffsetOf<CompositeVertex>(nameof(UV)))
        ]
    };
}
