//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Microsoft.JSInterop;

namespace noz;

public class WebGLRender : IRender
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private RenderBackendConfig _config = null!;

    public WebGLRender(IJSRuntime js)
    {
        _js = js;
    }

    public void Init(RenderBackendConfig config)
    {
        _config = config;
        // JS module initialization happens async in InitAsync
    }

    public async Task InitAsync(RenderBackendConfig config)
    {
        _config = config;
        _module = await _js.InvokeAsync<IJSObjectReference>("import", "./js/noz-webgl.js");
        await _module.InvokeVoidAsync("init");
    }

    public void Shutdown()
    {
        _module?.InvokeVoidAsync("shutdown");
    }

    public void BeginFrame()
    {
        _module?.InvokeVoidAsync("beginFrame");
    }

    public void EndFrame()
    {
        _module?.InvokeVoidAsync("endFrame");
    }

    public void Clear(Color color)
    {
        _module?.InvokeVoidAsync("clear", color.R, color.G, color.B, color.A);
    }

    public void SetViewport(int x, int y, int width, int height)
    {
        _module?.InvokeVoidAsync("setViewport", x, y, width, height);
    }
}
