//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.WebGPU;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    public void BeginScenePass(Color clearColor)
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("BeginScenePass called while already in a render pass");

        if (_commandEncoder == null)
            throw new InvalidOperationException("Command encoder is null - BeginFrame not called?");

        if (_currentSurfaceTextureView == null)
            throw new InvalidOperationException("Surface texture view is null - BeginFrame not called?");

        // Reset all cached state — new render pass encoder needs everything rebound
        _state = default;
        _state.CurrentPassSampleCount = 1;
        _state.HasDepthAttachment = true;
        _state.PipelineDirty = true;
        _state.BindGroupDirty = true;
        _currentGlobalsIndex = -1;

        var colorAttachment = new RenderPassColorAttachment
        {
            View = _currentSurfaceTextureView,
            ResolveTarget = null,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Silk.NET.WebGPU.Color
            {
                R = clearColor.R,
                G = clearColor.G,
                B = clearColor.B,
                A = clearColor.A
            }
        };

        var depthAttachment = new RenderPassDepthStencilAttachment
        {
            View = _depthTextureView,
            DepthLoadOp = LoadOp.Clear,
            DepthStoreOp = StoreOp.Store,
            DepthClearValue = 1.0f,
            DepthReadOnly = false,
            StencilLoadOp = LoadOp.Undefined,
            StencilStoreOp = StoreOp.Undefined,
            StencilClearValue = 0,
            StencilReadOnly = true,
        };

        var desc = new RenderPassDescriptor
        {
            ColorAttachments = &colorAttachment,
            ColorAttachmentCount = 1,
            DepthStencilAttachment = &depthAttachment
        };

        _currentRenderPass = _wgpu.CommandEncoderBeginRenderPass(_commandEncoder, in desc);

        // Debug group for RenderDoc
        fixed (byte* label = "ScenePass\0"u8)
        {
            _wgpu.RenderPassEncoderPushDebugGroup(_currentRenderPass, label);
        }

        _wgpu.RenderPassEncoderSetViewport(_currentRenderPass, 0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, (uint)_surfaceWidth, (uint)_surfaceHeight);
    }

    public void EndScenePass()
    {
        if (_currentRenderPass == null)
            throw new InvalidOperationException("EndScenePass called without matching BeginScenePass");

        _wgpu.RenderPassEncoderPopDebugGroup(_currentRenderPass);
        _wgpu.RenderPassEncoderEnd(_currentRenderPass);
        _wgpu.RenderPassEncoderRelease(_currentRenderPass);
        _currentRenderPass = null;

        if (_bindGroupsToRelease.Count > 0)
        {
            foreach (var bindGroup in _bindGroupsToRelease)
                _wgpu.BindGroupRelease((BindGroup*)bindGroup);
            _bindGroupsToRelease.Clear();
        }

        _currentBindGroup = null;
    }

    public void ResumeScenePass()
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("ResumeScenePass called while already in a render pass");

        if (_commandEncoder == null)
            throw new InvalidOperationException("Command encoder is null - BeginFrame not called?");

        if (_currentSurfaceTextureView == null)
            throw new InvalidOperationException("Surface texture view is null - BeginFrame not called?");

        // Reset all cached state — new render pass encoder needs everything rebound
        _state = default;
        _state.CurrentPassSampleCount = 1;
        _state.HasDepthAttachment = true;
        _state.PipelineDirty = true;
        _state.BindGroupDirty = true;
        _currentGlobalsIndex = -1;

        var colorAttachment = new RenderPassColorAttachment
        {
            View = _currentSurfaceTextureView,
            ResolveTarget = null,
            LoadOp = LoadOp.Load,
            StoreOp = StoreOp.Store,
        };

        var depthAttachment = new RenderPassDepthStencilAttachment
        {
            View = _depthTextureView,
            DepthLoadOp = LoadOp.Load,
            DepthStoreOp = StoreOp.Store,
            DepthClearValue = 1.0f,
            DepthReadOnly = false,
            StencilLoadOp = LoadOp.Undefined,
            StencilStoreOp = StoreOp.Undefined,
            StencilClearValue = 0,
            StencilReadOnly = true,
        };

        var desc = new RenderPassDescriptor
        {
            ColorAttachments = &colorAttachment,
            ColorAttachmentCount = 1,
            DepthStencilAttachment = &depthAttachment
        };

        _currentRenderPass = _wgpu.CommandEncoderBeginRenderPass(_commandEncoder, in desc);

        fixed (byte* label = "ScenePass (resumed)\0"u8)
        {
            _wgpu.RenderPassEncoderPushDebugGroup(_currentRenderPass, label);
        }

        _wgpu.RenderPassEncoderSetViewport(_currentRenderPass, 0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, (uint)_surfaceWidth, (uint)_surfaceHeight);
    }

    public void BeginDepthOnlyPass(nuint depthTexture, int width, int height)
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("BeginDepthOnlyPass called while already in a render pass");

        if (_commandEncoder == null)
            throw new InvalidOperationException("Command encoder is null - BeginFrame not called?");

        ref var dt = ref _depthTextures[(int)depthTexture];

        // Reset all cached state
        _state = default;
        _state.CurrentPassSampleCount = 1;
        _state.HasDepthAttachment = true;
        _state.IsDepthOnly = true;
        _state.PipelineDirty = true;
        _state.BindGroupDirty = true;
        _currentGlobalsIndex = -1;

        var depthAttachment = new RenderPassDepthStencilAttachment
        {
            View = dt.RenderView,
            DepthLoadOp = LoadOp.Clear,
            DepthStoreOp = StoreOp.Store,
            DepthClearValue = 1.0f,
            DepthReadOnly = false,
            StencilLoadOp = LoadOp.Undefined,
            StencilStoreOp = StoreOp.Undefined,
            StencilClearValue = 0,
            StencilReadOnly = true,
        };

        var desc = new RenderPassDescriptor
        {
            ColorAttachments = null,
            ColorAttachmentCount = 0,
            DepthStencilAttachment = &depthAttachment
        };

        _currentRenderPass = _wgpu.CommandEncoderBeginRenderPass(_commandEncoder, in desc);

        fixed (byte* label = "DepthOnlyPass\0"u8)
        {
            _wgpu.RenderPassEncoderPushDebugGroup(_currentRenderPass, label);
        }

        _wgpu.RenderPassEncoderSetViewport(_currentRenderPass, 0, 0, width, height, 0, 1);
        _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, (uint)width, (uint)height);
    }

    public void EndDepthOnlyPass()
    {
        if (_currentRenderPass == null)
            throw new InvalidOperationException("EndDepthOnlyPass called without matching BeginDepthOnlyPass");

        _wgpu.RenderPassEncoderPopDebugGroup(_currentRenderPass);
        _wgpu.RenderPassEncoderEnd(_currentRenderPass);
        _wgpu.RenderPassEncoderRelease(_currentRenderPass);
        _currentRenderPass = null;

        if (_bindGroupsToRelease.Count > 0)
        {
            foreach (var bindGroup in _bindGroupsToRelease)
                _wgpu.BindGroupRelease((BindGroup*)bindGroup);
            _bindGroupsToRelease.Clear();
        }

        _currentBindGroup = null;
        _state.IsDepthOnly = false;
    }
}
