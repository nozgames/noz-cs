//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.WebGPU;
using WGPUTexture = Silk.NET.WebGPU.Texture;
using WGPUTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    public void ResizeOffscreenTarget(int width, int height, int msaaSamples)
    {
        // Destroy existing offscreen resources
        DestroyOffscreenTarget();

        _offscreenWidth = width;
        _offscreenHeight = height;
        _msaaSamples = msaaSamples;

        // Create MSAA color texture (for rendering)
        var msaaDesc = new TextureDescriptor
        {
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            MipLevelCount = 1,
            SampleCount = (uint)msaaSamples,
            Dimension = TextureDimension.Dimension2D,
            Format = _surfaceFormat,
            Usage = TextureUsage.RenderAttachment,
        };
        _offscreenMsaaTexture = _wgpu.DeviceCreateTexture(_device, &msaaDesc);
        _offscreenMsaaTextureView = _wgpu.TextureCreateView(_offscreenMsaaTexture, null);

        // Create resolve texture (for sampling in composite)
        var resolveDesc = new TextureDescriptor
        {
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            MipLevelCount = 1,
            SampleCount = 1,
            Dimension = TextureDimension.Dimension2D,
            Format = _surfaceFormat,
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
        };
        _offscreenResolveTexture = _wgpu.DeviceCreateTexture(_device, &resolveDesc);
        _offscreenResolveTextureView = _wgpu.TextureCreateView(_offscreenResolveTexture, null);

        // Create depth/stencil texture
        var depthDesc = new TextureDescriptor
        {
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            MipLevelCount = 1,
            SampleCount = (uint)msaaSamples,
            Dimension = TextureDimension.Dimension2D,
            Format = WGPUTextureFormat.Depth24PlusStencil8,
            Usage = TextureUsage.RenderAttachment,
        };
        _offscreenDepthTexture = _wgpu.DeviceCreateTexture(_device, &depthDesc);
        _offscreenDepthTextureView = _wgpu.TextureCreateView(_offscreenDepthTexture, null);
    }

    public void BeginScenePass(Color clearColor)
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("BeginScenePass called while already in a render pass");

        var colorAttachment = new RenderPassColorAttachment
        {
            View = _offscreenMsaaTextureView,
            ResolveTarget = _offscreenResolveTextureView, // MSAA resolve
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
            View = _offscreenDepthTextureView,
            DepthLoadOp = LoadOp.Clear,
            DepthStoreOp = StoreOp.Store,
            DepthClearValue = 1.0f,
            StencilLoadOp = LoadOp.Clear,
            StencilStoreOp = StoreOp.Store,
            StencilClearValue = 0,
        };

        var desc = new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment,
            DepthStencilAttachment = &depthAttachment,
        };

        _currentRenderPass = _wgpu.CommandEncoderBeginRenderPass(_commandEncoder, &desc);
        _inRenderPass = true;

        // Set default viewport and scissor
        _wgpu.RenderPassEncoderSetViewport(_currentRenderPass, 0, 0, _offscreenWidth, _offscreenHeight, 0, 1);
        _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, (uint)_offscreenWidth, (uint)_offscreenHeight);
    }

    public void EndScenePass()
    {
        if (_currentRenderPass == null)
            throw new InvalidOperationException("EndScenePass called without matching BeginScenePass");

        _wgpu.RenderPassEncoderEnd(_currentRenderPass);
        _wgpu.RenderPassEncoderRelease(_currentRenderPass);
        _currentRenderPass = null;
        _inRenderPass = false;

        // MSAA resolve happens automatically via ResolveTarget
    }

    public void Composite(nuint compositeShader)
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("Composite called while in a render pass");

        // Get current surface texture
        SurfaceTexture surfaceTexture;
        _wgpu.SurfaceGetCurrentTexture(_surface, &surfaceTexture);

        if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success)
        {
            throw new Exception($"Failed to get current surface texture: {surfaceTexture.Status}");
        }

        var swapChainView = _wgpu.TextureCreateView(surfaceTexture.Texture, null);

        // Begin render pass to swap chain
        var colorAttachment = new RenderPassColorAttachment
        {
            View = swapChainView,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Silk.NET.WebGPU.Color { R = 0, G = 0, B = 0, A = 1 }
        };

        var desc = new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment,
        };

        _currentRenderPass = _wgpu.CommandEncoderBeginRenderPass(_commandEncoder, &desc);

        // Set viewport to full swap chain
        _wgpu.RenderPassEncoderSetViewport(_currentRenderPass, 0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, (uint)_surfaceWidth, (uint)_surfaceHeight);

        // Bind composite shader and render fullscreen quad
        BindShader(compositeShader);

        // Create temporary texture handle for offscreen resolve texture
        // This is a bit of a hack - we need to bind the resolve texture but it's not in our texture array
        // For now, we'll need to create a bind group manually for the composite pass
        CreateAndBindCompositeBindGroup(compositeShader);

        // Draw fullscreen quad (will need to be created separately)
        // For now, just end the pass - this will be completed when we add the fullscreen quad mesh

        _wgpu.RenderPassEncoderEnd(_currentRenderPass);
        _wgpu.RenderPassEncoderRelease(_currentRenderPass);
        _currentRenderPass = null;

        // Release swap chain view
        _wgpu.TextureViewRelease(swapChainView);
    }

    private void CreateAndBindCompositeBindGroup(nuint shaderHandle)
    {
        ref var shader = ref _shaders[(int)shaderHandle];

        // Create sampler for composite texture
        var samplerDesc = new SamplerDescriptor
        {
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
        };
        var sampler = _wgpu.DeviceCreateSampler(_device, &samplerDesc);

        // Build bind group entries for composite shader
        var entries = stackalloc BindGroupEntry[2];

        // Binding 0: Scene texture
        entries[0] = new BindGroupEntry
        {
            Binding = 0,
            TextureView = _offscreenResolveTextureView,
        };

        // Binding 1: Sampler
        entries[1] = new BindGroupEntry
        {
            Binding = 1,
            Sampler = sampler,
        };

        // Release old bind group if exists
        if (_currentBindGroup != null)
        {
            _wgpu.BindGroupRelease(_currentBindGroup);
            _currentBindGroup = null;
        }

        // Create bind group
        var desc = new BindGroupDescriptor
        {
            Layout = shader.BindGroupLayout0,
            EntryCount = 2,
            Entries = entries,
        };
        _currentBindGroup = _wgpu.DeviceCreateBindGroup(_device, &desc);

        // Bind to render pass
        _wgpu.RenderPassEncoderSetBindGroup(_currentRenderPass, 0, _currentBindGroup, 0, null);

        // Clean up sampler
        _wgpu.SamplerRelease(sampler);
    }
}
