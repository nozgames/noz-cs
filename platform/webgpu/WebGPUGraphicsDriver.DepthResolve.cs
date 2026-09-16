//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WGPUTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    private RenderPipeline* _depthResolvePipeline;
    private BindGroupLayout* _depthResolveLayout;

    // WebGPU resolves multisampled color attachments, but not depth. Preserve
    // the nearest sample (normal 0..1 depth) instead of averaging unrelated
    // surfaces into a depth that has no corresponding geometry.
    private const string DepthResolveSource = """
        @group(0) @binding(0) var source: texture_depth_multisampled_2d;
        @vertex fn vs_main(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {
            let p = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
            return vec4<f32>(p * 2.0 - vec2<f32>(1.0), 0.0, 1.0);
        }
        @fragment fn fs_main(@builtin(position) p: vec4<f32>) -> @builtin(frag_depth) f32 {
            var depth = 1.0;
            for (var i = 0u; i < textureNumSamples(source); i++) {
                depth = min(depth, textureLoad(source, vec2<i32>(p.xy), i32(i)));
            }
            return depth;
        }
        """;

    private BindGroup* CreateDepthResolveBinding(TextureView* source)
    {
        if (_depthResolvePipeline == null)
        {
            var entry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Depth,
                    ViewDimension = TextureViewDimension.Dimension2D,
                    Multisampled = true,
                },
            };
            var layoutDesc = new BindGroupLayoutDescriptor { EntryCount = 1, Entries = &entry };
            _depthResolveLayout = _wgpu.DeviceCreateBindGroupLayout(_device, &layoutDesc);
            var layout = CreatePipelineLayout(_depthResolveLayout);
            var module = CreateShaderModule(DepthResolveSource + "\0", "Depth resolve");
            using var vs = SilkMarshal.StringToMemory("vs_main");
            using var fs = SilkMarshal.StringToMemory("fs_main");
            using var label = SilkMarshal.StringToMemory("Nearest-sample depth resolve");
            var fragment = new FragmentState { Module = module, EntryPoint = (byte*)fs };
            var stencil = new StencilFaceState
            {
                Compare = CompareFunction.Always,
                FailOp = StencilOperation.Keep,
                DepthFailOp = StencilOperation.Keep,
                PassOp = StencilOperation.Keep,
            };
            var depth = new DepthStencilState
            {
                Format = WGPUTextureFormat.Depth24Plus,
                DepthWriteEnabled = true,
                DepthCompare = CompareFunction.Always,
                StencilFront = stencil,
                StencilBack = stencil,
            };
            var desc = new RenderPipelineDescriptor
            {
                Label = (byte*)label,
                Layout = layout,
                Vertex = new VertexState { Module = module, EntryPoint = (byte*)vs },
                Fragment = &fragment,
                Primitive = new PrimitiveState
                {
                    Topology = PrimitiveTopology.TriangleList,
                    FrontFace = FrontFace.Ccw,
                    CullMode = CullMode.None,
                },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                DepthStencil = &depth,
            };
            _depthResolvePipeline = _wgpu.DeviceCreateRenderPipeline(_device, &desc);
            _wgpu.ShaderModuleRelease(module);
            _wgpu.PipelineLayoutRelease(layout);
        }

        var binding = new BindGroupEntry { Binding = 0, TextureView = source };
        var bindingDesc = new BindGroupDescriptor { Layout = _depthResolveLayout, EntryCount = 1, Entries = &binding };
        return _wgpu.DeviceCreateBindGroup(_device, &bindingDesc);
    }

    private void ResolveDepth(in RenderTextureInfo rt)
    {
        if (rt.DepthResolveBinding == null) return;
        var depth = new RenderPassDepthStencilAttachment
        {
            View = _textures[(int)rt.DepthTextureHandle].TextureView,
            DepthLoadOp = LoadOp.Clear,
            DepthStoreOp = StoreOp.Store,
            DepthClearValue = 1,
            StencilLoadOp = LoadOp.Undefined,
            StencilStoreOp = StoreOp.Undefined,
            StencilReadOnly = true,
        };
        var desc = new RenderPassDescriptor { DepthStencilAttachment = &depth };
        var pass = _wgpu.CommandEncoderBeginRenderPass(_commandEncoder, &desc);
        _wgpu.RenderPassEncoderSetPipeline(pass, _depthResolvePipeline);
        _wgpu.RenderPassEncoderSetBindGroup(pass, 0, rt.DepthResolveBinding, 0, null);
        _wgpu.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
        _wgpu.RenderPassEncoderEnd(pass);
        _wgpu.RenderPassEncoderRelease(pass);
    }

    private void DestroyDepthResolvePipeline()
    {
        if (_depthResolvePipeline != null) _wgpu.RenderPipelineRelease(_depthResolvePipeline);
        if (_depthResolveLayout != null) _wgpu.BindGroupLayoutRelease(_depthResolveLayout);
        _depthResolvePipeline = null;
        _depthResolveLayout = null;
    }
}
