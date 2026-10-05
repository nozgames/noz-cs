//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.WebGPU;
using NoZ.Platform;
using WGPUBuffer = Silk.NET.WebGPU.Buffer;
using WGPUBufferUsage = Silk.NET.WebGPU.BufferUsage;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    private static readonly ProfilerCounter s_counterBindGroupCreations = new("WebGPU.BindGroupCreations");
    private const string GlobalsBindingName = "globals";

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
        // Store uniform data by name - will be written to per-shader buffers when bind groups are created
        if (!_uniformData.TryGetValue(name, out var existing) || existing.Length != data.Length)
        {
            _uniformData[name] = new byte[data.Length];

            // A bind group made for the old size binds the wrong range.
            if (existing != null) ForgetAllBindGroups();
        }

        data.CopyTo(_uniformData[name]);
        _state.BindGroupDirty = true;
    }

    public void SetGlobalsCount(int count)
    {
        if (count < 0 || count > _maxGlobals)
            throw new InvalidOperationException(
                $"WebGPU global snapshot budget exhausted ({_maxGlobals}). " +
                $"Increase {nameof(GraphicsConfig)}.{nameof(GraphicsConfig.MaxGlobalSnapshots)}.");

        if (count > _globalsCount) _globalsCount = count;
        if (count <= _globalsCapacity) return;

        var capacity = Math.Max(_globalsCapacity, MinGlobalsCapacity);
        while (capacity < count) capacity *= 2;
        capacity = Math.Min(capacity, _maxGlobals);

        var descriptor = new BufferDescriptor
        {
            Size = (ulong)capacity * GlobalsSlotSize,
            Usage = WGPUBufferUsage.Uniform | WGPUBufferUsage.CopyDst,
            MappedAtCreation = false
        };
        var buffer = _wgpu.DeviceCreateBuffer(_device, &descriptor);

        // Draws already recorded keep the old buffer and what was written to it. The
        // slots staged so far this frame go to the new one as well.
        if (_globalsBuffer != null)
        {
            FlushGlobals();
            _wgpu.BufferRelease(_globalsBuffer);
            if (_globalsCapacity > 0)
            {
                _globalsDirtyFrom = 0;
                _globalsDirtyTo = _globalsCapacity - 1;
            }
        }

        Array.Resize(ref _globalsStaging, capacity * GlobalsSlotSize);
        _globalsBuffer = buffer;
        _globalsCapacity = capacity;
        _globalsGeneration++;
        ForgetAllBindGroups();
    }

    public void SetGlobals(int index, ReadOnlySpan<byte> data)
    {
        if (index < 0 || index >= _globalsCapacity)
            return;

        if (data.Length > GlobalsSlotSize)
            throw new ArgumentException($"A globals snapshot is at most {GlobalsSlotSize} bytes.", nameof(data));

        data.CopyTo(_globalsStaging.AsSpan(index * GlobalsSlotSize, GlobalsSlotSize));
        if (index < _globalsDirtyFrom) _globalsDirtyFrom = index;
        if (index > _globalsDirtyTo) _globalsDirtyTo = index;
    }

    // The slots set since the last flush go to the buffer in one write, before a draw
    // reads them.
    private void FlushGlobals()
    {
        if (_globalsDirtyTo < _globalsDirtyFrom || _globalsBuffer == null)
            return;

        var offset = _globalsDirtyFrom * GlobalsSlotSize;
        var size = (_globalsDirtyTo - _globalsDirtyFrom + 1) * GlobalsSlotSize;
        fixed (byte* staged = _globalsStaging)
        {
            _wgpu.QueueWriteBuffer(_queue, _globalsBuffer, (ulong)offset, staged + offset, (nuint)size);
        }

        _globalsDirtyFrom = int.MaxValue;
        _globalsDirtyTo = -1;
    }

    public void BindGlobals(int index)
    {
        if (_currentGlobalsIndex == index)
            return;

        _currentGlobalsIndex = index;
        _state.GlobalsDirty = true;
    }

    private void ForgetAllBindGroups()
    {
        s_counterBindGroupRelease.Increment(_bindGroupCache.Count);
        foreach (var group in _bindGroupCache.Values)
            _wgpu.BindGroupRelease((BindGroup*)group);
        _bindGroupCache.Clear();
        _currentBindGroup = null;
        _state.BindGroupDirty = true;
    }

    // A texture or shader that is going away takes the bind groups made with it along: its
    // handle will be handed out again.
    private void ForgetBindGroups(nuint shader, nuint texture)
    {
        if (_bindGroupCache.Count == 0)
            return;

        _bindGroupsToForget.Clear();
        foreach (var key in _bindGroupCache.Keys)
            if ((shader != 0 && key.Shader == shader) || (texture != 0 && key.Uses(texture)))
                _bindGroupsToForget.Add(key);

        if (_bindGroupsToForget.Count == 0)
            return;

        s_counterBindGroupRelease.Increment(_bindGroupsToForget.Count);
        foreach (var key in _bindGroupsToForget)
        {
            _wgpu.BindGroupRelease((BindGroup*)_bindGroupCache[key]);
            _bindGroupCache.Remove(key);
        }

        _bindGroupsToForget.Clear();
        _currentBindGroup = null;
        _state.BindGroupDirty = true;
    }

    public void DrawElements(int firstIndex, int indexCount, int baseVertex = 0)
        => DrawIndexed(firstIndex, indexCount, baseVertex, 1, 0);

    public void DrawElementsInstanced(int firstIndex, int indexCount, int instanceCount, int firstInstance)
        => DrawIndexed(firstIndex, indexCount, 0, instanceCount, firstInstance);

    public void BindInstanceStream(nuint stream)
    {
        if (_state.BoundInstanceStream == stream) return;
        _state.BoundInstanceStream = stream;
        _state.PipelineDirty = true;
    }

    private void DrawIndexed(int firstIndex, int indexCount, int baseVertex, int instanceCount, int firstInstance)
    {
        if (_currentRenderPass == null)
            throw new InvalidOperationException("DrawElements called outside of render pass");

        // Update pipeline if shader/blend/vertex format changed
        if (_state.PipelineDirty)
        {
            var pipeline = GetOrCreatePipeline(
                _state.BoundShader,
                _state.BlendMode,
                _meshes[(int)_state.BoundMesh].Stride
            );

            if (pipeline == null)
            {
                Log.Error($"Cannot draw - pipeline is null for shader {_state.BoundShader}");
                return;
            }

            _wgpu.RenderPassEncoderSetPipeline(_currentRenderPass, pipeline);
            _state.PipelineDirty = false;
        }

        if (_globalsDirtyTo >= _globalsDirtyFrom)
            FlushGlobals();

        // Update bind group if textures/buffers changed
        UpdateBindGroupIfNeeded();

        // Bind vertex and index buffers
        ref var mesh = ref _meshes[(int)_state.BoundMesh];
        _wgpu.RenderPassEncoderSetVertexBuffer(_currentRenderPass, 0, mesh.VertexBuffer, 0, (ulong)(mesh.MaxVertices * mesh.Stride));
        if (_state.BoundInstanceStream != 0)
        {
            ref var stream = ref _meshes[(int)_state.BoundInstanceStream];
            _wgpu.RenderPassEncoderSetVertexBuffer(_currentRenderPass, 1, stream.VertexBuffer, 0, (ulong)(stream.MaxVertices * stream.Stride));
        }
        var indexFormat = mesh.IndexFormat == MeshIndexFormat.UInt32 ? IndexFormat.Uint32 : IndexFormat.Uint16;
        var indexStride = mesh.IndexFormat == MeshIndexFormat.UInt32 ? sizeof(uint) : sizeof(ushort);
        _wgpu.RenderPassEncoderSetIndexBuffer(_currentRenderPass, mesh.IndexBuffer, indexFormat, 0, (ulong)(mesh.MaxIndices * indexStride));

        if (_state.ScissorEnabled)
        {
            var sy = _state.Viewport.Height - _state.Scissor.Y - _state.Scissor.Height;
            var sh = _state.Scissor.Height;
            var sx = _state.Scissor.X;
            var sw = _state.Scissor.Width;
            if (sy < 0) { sh += sy; sy = 0; }
            if (sx < 0) { sw += sx; sx = 0; }
            
            if (sw <= 0 || sh <= 0)
                _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, 0, 0);
            else
                _wgpu.RenderPassEncoderSetScissorRect(
                    _currentRenderPass,
                    (uint)sx,
                    (uint)sy,
                    (uint)Math.Min(sw, _state.Viewport.Width - sx),
                    (uint)Math.Min(sh, _state.Viewport.Height - sy));
        }
        else
        {
            // Use render texture dimensions if rendering to texture, otherwise surface dimensions
            uint width, height;
            if (_activeRenderTexture != 0)
            {
                ref var rt = ref _renderTextures[_rtHandleToSlot[(int)_activeRenderTexture]];
                width = (uint)rt.Width;
                height = (uint)rt.Height;
            }
            else
            {
                width = (uint)_surfaceWidth;
                height = (uint)_surfaceHeight;
            }
            _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, width, height);
        }

        // Draw indexed
        _wgpu.RenderPassEncoderDrawIndexed(
            _currentRenderPass,
            (uint)indexCount,
            (uint)instanceCount,
            (uint)firstIndex,
            baseVertex,
            (uint)firstInstance
        );
    }

    // What a bind group for the bound shader is made from: the shader, the globals buffer
    // and the textures and filters in the slots the shader reads. Other slots may hold
    // what an earlier draw left there.
    private BindGroupKey MakeBindGroupKey(ref ShaderInfo shader)
    {
        var key = new BindGroupKey
        {
            Shader = _state.BoundShader,
            GlobalsGeneration = shader.HasGlobals ? _globalsGeneration : 0,
        };

        var slots = Math.Min(shader.TextureSlots?.Count ?? 0, 8);
        for (var i = 0; i < slots; i++)
        {
            key.Textures[i] = _state.BoundTextures[i];
            key.Filters |= (ulong)_state.TextureFilters[i] << (i * 8);
        }

        return key;
    }

    private void UpdateBindGroupIfNeeded()
    {
        if (!_state.BindGroupDirty && !_state.GlobalsDirty)
            return;

        ref var shader = ref _shaders[(int)_state.BoundShader];

        if (shader.BindGroupEntryCount == 0)
        {
            _state.BindGroupDirty = false;
            _state.GlobalsDirty = false;
            return;
        }

        if (_state.BindGroupDirty && !FindOrCreateBindGroup(ref shader))
        {
            _state.BindGroupDirty = false;
            _state.GlobalsDirty = false;
            return;
        }

        _state.BindGroupDirty = false;
        _state.GlobalsDirty = false;

        if (_currentRenderPass == null || _currentBindGroup == null)
            return;

        // The same bind group serves every draw of the shader with these textures: only
        // where its globals start changes from draw to draw.
        if (shader.HasGlobals)
        {
            if (_currentGlobalsIndex < 0 || _currentGlobalsIndex >= _globalsCapacity)
            {
                Log.Error($"Globals index {_currentGlobalsIndex} out of range!");
                return;
            }

            var offset = (uint)(_currentGlobalsIndex * GlobalsSlotSize);
            _wgpu.RenderPassEncoderSetBindGroup(_currentRenderPass, 0, _currentBindGroup, 1, &offset);
        }
        else
        {
            _wgpu.RenderPassEncoderSetBindGroup(_currentRenderPass, 0, _currentBindGroup, 0, null);
        }
    }

    private bool FindOrCreateBindGroup(ref ShaderInfo shader)
    {
        var bindings = shader.Bindings;
        _currentBindGroup = null;

        if (bindings == null || bindings.Count == 0)
        {
            Log.Error("Shader has no binding metadata!");
            return false;
        }

        // Write uniform data before cache check — data changes don't affect cache key
        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (binding.Type != ShaderBindingType.UniformBuffer || binding.Name == "globals")
                continue;

            if (!_uniformData.TryGetValue(binding.Name, out var uniformData))
                continue;

            if (!shader.UniformBuffers.TryGetValue(binding.Name, out var bufferPtr) || bufferPtr == 0)
            {
                var bufferDesc = new BufferDescriptor
                {
                    Size = (ulong)uniformData.Length,
                    Usage = WGPUBufferUsage.Uniform | WGPUBufferUsage.CopyDst,
                    MappedAtCreation = false,
                };
                var buffer = _wgpu.DeviceCreateBuffer(_device, &bufferDesc);
                shader.UniformBuffers[binding.Name] = (nint)buffer;
                bufferPtr = (nint)buffer;
            }

            fixed (byte* dataPtr = uniformData)
            {
                _wgpu.QueueWriteBuffer(_queue, (WGPUBuffer*)bufferPtr, 0, dataPtr, (nuint)uniformData.Length);
            }
        }

        // Cache check — keyed on resource references, not buffer contents
        var cacheKey = MakeBindGroupKey(ref shader);
        if (_bindGroupCache.TryGetValue(cacheKey, out var cached))
        {
            _currentBindGroup = (BindGroup*)cached;
            return true;
        }

        // Cache miss — create bind group
        var entries = stackalloc BindGroupEntry[bindings.Count];
        int validEntryCount = 0;

        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];

            switch (binding.Type)
            {
                case ShaderBindingType.UniformBuffer:
                {
                    WGPUBuffer* buffer;
                    ulong bufferSize;

                    if (binding.Name == "globals")
                    {
                        if (_globalsBuffer == null)
                        {
                            Log.Error("No globals have been set!");
                            return false;
                        }

                        // One slot's worth, from wherever a draw's dynamic offset puts it.
                        buffer = _globalsBuffer;
                        bufferSize = GlobalsSlotSize;
                    }
                    else
                    {
                        if (!shader.UniformBuffers.TryGetValue(binding.Name, out var bufferPtr) || bufferPtr == 0)
                        {
                            Log.Error($"Uniform buffer for '{binding.Name}' not created!");
                            return false;
                        }
                        buffer = (WGPUBuffer*)bufferPtr;
                        bufferSize = (ulong)_uniformData[binding.Name].Length;
                    }

                    entries[validEntryCount++] = new BindGroupEntry
                    {
                        Binding = binding.Binding,
                        Buffer = buffer,
                        Offset = 0,
                        Size = bufferSize,
                    };
                    break;
                }

                case ShaderBindingType.Texture2D:
                case ShaderBindingType.Texture2DArray:
                case ShaderBindingType.Texture2DUnfilterable:
                case ShaderBindingType.TextureDepth:
                {
                    int textureSlot = GetTextureSlotForBinding(binding.Binding, ref shader);
                    nuint textureHandle = textureSlot >= 0 ? (nuint)_state.BoundTextures[textureSlot] : 0;

                    if (textureHandle == 0)
                    {
                        Log.Error($"Texture slot {textureSlot} (binding {binding.Binding}) not bound!");
                        return false;
                    }

                    ref var tex = ref _textures[(int)textureHandle];
                    var view = (binding.Type != ShaderBindingType.Texture2DArray && tex.TextureView2D != null)
                        ? tex.TextureView2D : tex.TextureView;
                    entries[validEntryCount++] = new BindGroupEntry
                    {
                        Binding = binding.Binding,
                        TextureView = view,
                    };
                    break;
                }

                case ShaderBindingType.Sampler:
                {
                    int textureSlot = GetTextureSlotForBinding(binding.Binding, ref shader);
                    var slotFilter = textureSlot >= 0 ? (TextureFilter)_state.TextureFilters[textureSlot] : TextureFilter.Point;
                    var sampler = slotFilter == TextureFilter.Point ? _nearestSampler : _linearSampler;
                    entries[validEntryCount++] = new BindGroupEntry
                    {
                        Binding = binding.Binding,
                        Sampler = sampler,
                    };
                    break;
                }
            }
        }

        var desc = new BindGroupDescriptor
        {
            Layout = shader.BindGroupLayout0,
            EntryCount = (uint)validEntryCount,
            Entries = entries,
        };
        var created = _wgpu.DeviceCreateBindGroup(_device, &desc);

        s_counterBindGroupCreations.Increment(1);

        if (created == null)
        {
            Log.Error("Failed to create bind group!");
            return false;
        }

        // What is bound changes without end in some programs (a texture a frame, say):
        // start over rather than grow for good.
        if (_bindGroupCache.Count >= MaxCachedBindGroups)
            ForgetAllBindGroups();

        _bindGroupCache[cacheKey] = (nint)created;
        _currentBindGroup = created;
        return true;
    }

    private int GetTextureSlotForBinding(uint bindingNumber, ref ShaderInfo shader)
    {
        for (int i = 0; i < shader.TextureSlots.Count; i++)
        {
            var slot = shader.TextureSlots[i];
            if (slot.TextureBinding == bindingNumber || slot.SamplerBinding == bindingNumber)
                return i;
        }
        return -1;
    }
}
