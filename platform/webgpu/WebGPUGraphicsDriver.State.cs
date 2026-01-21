//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.WebGPU;
using NoZ.Platform;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    public void SetBlendMode(BlendMode mode)
    {
        if (_state.BlendMode == mode)
            return;

        _state.BlendMode = mode;
        _state.PipelineDirty = true;
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
            _wgpu.RenderPassEncoderSetPipeline(_currentRenderPass, pipeline);
            _state.PipelineDirty = false;
        }

        // Update bind group if textures/buffers changed
        UpdateBindGroupIfNeeded();

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

    private void UpdateBindGroupIfNeeded()
    {
        // Check if bound resources changed
        if (!_state.BindGroupDirty)
            return;

        ref var shader = ref _shaders[(int)_state.BoundShader];
        int entryCount = shader.BindGroupEntryCount;

        Log.Debug($"UpdateBindGroup: EntryCount={entryCount}, BoundTexture0={_state.BoundTexture0}, BoundBoneTexture={_state.BoundBoneTexture}");

        // Build bind group entries based on what the shader expects
        var entries = stackalloc BindGroupEntry[entryCount];

        // Binding 0: Globals uniform buffer (always present)
        if (_state.BoundUniformBuffer0 == 0)
        {
            Log.Error("BoundUniformBuffer0 is 0!");
            return;
        }

        entries[0] = new BindGroupEntry
        {
            Binding = 0,
            Buffer = _buffers[(int)_state.BoundUniformBuffer0].Buffer,
            Offset = 0,
            Size = (ulong)_buffers[(int)_state.BoundUniformBuffer0].SizeInBytes,
        };

        // Binding 1: Slot 0 texture (main atlas/texture) - always BoundTexture0
        if (entryCount >= 2)
        {
            if (_state.BoundTexture0 == 0)
            {
                Log.Error($"Binding 1: BoundTexture0 is 0!");
                return;
            }

            entries[1] = new BindGroupEntry
            {
                Binding = 1,
                TextureView = _textures[(int)_state.BoundTexture0].TextureView,
            };
        }

        // Binding 2: Sampler for binding 1
        if (entryCount >= 3)
        {
            entries[2] = new BindGroupEntry
            {
                Binding = 2,
                Sampler = _textures[(int)_state.BoundTexture0].Sampler,
            };
        }

        // Binding 3: Slot 1 texture (bone texture) or uniform buffer (text params)
        if (entryCount >= 4)
        {
            // Check what type binding 3 is from the shader
            // For now, assume it's the bone texture (BoundBoneTexture) for sprite shader
            // Or it could be a uniform buffer for text shader - TODO
            Console.WriteLine($"[WEBGPU] Binding 3: BoundBoneTexture={_state.BoundBoneTexture}");
            if (_state.BoundBoneTexture != 0)
            {
                var boneTextureView = _textures[(int)_state.BoundBoneTexture].TextureView;
                Console.WriteLine($"[WEBGPU]   Bone texture view: {(nint)boneTextureView:X}");

                entries[3] = new BindGroupEntry
                {
                    Binding = 3,
                    TextureView = boneTextureView,
                };
            }
            else
            {
                Console.WriteLine("[WEBGPU] WARNING: Binding 3 requested but BoundBoneTexture is 0");
            }
        }

        // Binding 4: Sampler for binding 3 (bone texture sampler)
        if (entryCount >= 5)
        {
            Console.WriteLine($"[WEBGPU] Binding 4: BoundBoneTexture={_state.BoundBoneTexture}");
            if (_state.BoundBoneTexture != 0)
            {
                var boneSampler = _textures[(int)_state.BoundBoneTexture].Sampler;
                Console.WriteLine($"[WEBGPU]   Bone sampler: {(nint)boneSampler:X}");

                entries[4] = new BindGroupEntry
                {
                    Binding = 4,
                    Sampler = boneSampler,
                };
            }
        }

        // Release old bind group if exists
        if (_currentBindGroup != null)
        {
            _wgpu.BindGroupRelease(_currentBindGroup);
            _currentBindGroup = null;
        }

        Console.WriteLine($"[WEBGPU] Creating bind group with {entryCount} entries...");

        // Debug log all entries
        for (int i = 0; i < entryCount; i++)
        {
            Console.WriteLine($"[WEBGPU]   Entry {i}: Binding={entries[i].Binding}, Buffer={(nint)entries[i].Buffer:X}, TextureView={(nint)entries[i].TextureView:X}, Sampler={(nint)entries[i].Sampler:X}");
        }
        Console.Out.Flush();

        // Create bind group
        var desc = new BindGroupDescriptor
        {
            Layout = shader.BindGroupLayout0,
            EntryCount = (uint)entryCount,
            Entries = entries,
        };
        _currentBindGroup = _wgpu.DeviceCreateBindGroup(_device, &desc);

        if (_currentBindGroup == null)
        {
            Log.Error("Failed to create bind group!");
            return;
        }

        Log.Debug($"Bind group created: {(nint)_currentBindGroup:X}");

        // Bind to render pass
        if (_currentRenderPass != null)
        {
            _wgpu.RenderPassEncoderSetBindGroup(_currentRenderPass, 0, _currentBindGroup, 0, null);
        }

        _state.BindGroupDirty = false;
    }
}
