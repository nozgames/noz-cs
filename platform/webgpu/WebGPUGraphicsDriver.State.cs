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
            _uniformData[name] = new byte[data.Length];

        data.CopyTo(_uniformData[name]);
        _state.BindGroupDirty = true;
    }

    public void SetGlobalsCount(int count)
    {
        // Ensure we have enough globals buffers allocated
        while (_globalsBufferCount < count)
        {
            var bufferDesc = new BufferDescriptor
            {
                Size = GlobalsBufferSize,
                Usage = WGPUBufferUsage.Uniform | WGPUBufferUsage.CopyDst,
                MappedAtCreation = false
            };
            _globalsBuffers[_globalsBufferCount] = _wgpu.DeviceCreateBuffer(_device, &bufferDesc);
            _globalsBufferCount++;
        }
    }

    public void SetGlobals(int index, ReadOnlySpan<byte> data)
    {
        if (index < 0 || index >= _globalsBufferCount)
            return;

        fixed (byte* dataPtr = data)
        {
            _wgpu.QueueWriteBuffer(_queue, _globalsBuffers[index], 0, dataPtr, (nuint)data.Length);
        }
    }

    public void BindGlobals(int index)
    {
        if (_currentGlobalsIndex == index)
            return;

        _currentGlobalsIndex = index;
        _state.BindGroupDirty = true;
    }

    public void DrawElements(int firstIndex, int indexCount, int baseVertex = 0)
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

        // Update bind group if textures/buffers changed
        UpdateBindGroupIfNeeded();

        // Bind vertex and index buffers
        ref var mesh = ref _meshes[(int)_state.BoundMesh];
        _wgpu.RenderPassEncoderSetVertexBuffer(_currentRenderPass, 0, mesh.VertexBuffer, 0, (ulong)(mesh.MaxVertices * mesh.Stride));
        _wgpu.RenderPassEncoderSetIndexBuffer(_currentRenderPass, mesh.IndexBuffer, IndexFormat.Uint16, 0, (ulong)(mesh.MaxIndices * sizeof(ushort)));

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
            1, // instance count
            (uint)firstIndex,
            baseVertex,
            0 // first instance
        );
    }

    private int ComputeBindGroupCacheKey()
    {
        var hash = new HashCode();
        hash.Add(_state.BoundShader);
        hash.Add(_currentGlobalsIndex);
        for (int i = 0; i < 8; i++)
        {
            hash.Add(_state.BoundTextures[i]);
            hash.Add(_state.TextureFilters[i]);
        }
        return hash.ToHashCode();
    }

    private void UpdateBindGroupIfNeeded()
    {
        if (!_state.BindGroupDirty)
            return;

        ref var shader = ref _shaders[(int)_state.BoundShader];
        var bindings = shader.Bindings;

        if (bindings == null || bindings.Count == 0)
        {
            Log.Error("Shader has no binding metadata!");
            _state.BindGroupDirty = false;
            return;
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
        var cacheKey = ComputeBindGroupCacheKey();
        if (_bindGroupCache.TryGetValue(cacheKey, out var cached))
        {
            _currentBindGroup = (BindGroup*)cached;
            if (_currentRenderPass != null)
                _wgpu.RenderPassEncoderSetBindGroup(_currentRenderPass, 0, _currentBindGroup, 0, null);
            _state.BindGroupDirty = false;
            return;
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
                        if (_currentGlobalsIndex < 0 || _currentGlobalsIndex >= _globalsBufferCount)
                        {
                            Log.Error($"Globals index {_currentGlobalsIndex} out of range!");
                            _state.BindGroupDirty = false;
                            return;
                        }
                        buffer = _globalsBuffers[_currentGlobalsIndex];
                        bufferSize = GlobalsBufferSize;
                    }
                    else
                    {
                        if (!shader.UniformBuffers.TryGetValue(binding.Name, out var bufferPtr) || bufferPtr == 0)
                        {
                            Log.Error($"Uniform buffer for '{binding.Name}' not created!");
                            _state.BindGroupDirty = false;
                            return;
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
                {
                    int textureSlot = GetTextureSlotForBinding(binding.Binding, ref shader);
                    nuint textureHandle = textureSlot >= 0 ? (nuint)_state.BoundTextures[textureSlot] : 0;

                    if (textureHandle == 0)
                    {
                        Log.Error($"Texture slot {textureSlot} (binding {binding.Binding}) not bound!");
                        _state.BindGroupDirty = false;
                        return;
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
        _currentBindGroup = _wgpu.DeviceCreateBindGroup(_device, &desc);

        s_counterBindGroupCreations.Increment(1);

        if (_currentBindGroup == null)
        {
            Log.Error("Failed to create bind group!");
            return;
        }

        _bindGroupCache[cacheKey] = (nint)_currentBindGroup;

        if (_currentRenderPass != null)
            _wgpu.RenderPassEncoderSetBindGroup(_currentRenderPass, 0, _currentBindGroup, 0, null);

        _state.BindGroupDirty = false;
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
