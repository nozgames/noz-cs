//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using CoreAnimation;
using Foundation;
using NoZ.Platform.WebGPU;
using static SDL.SDL3;

namespace NoZ.Platform;

public static class iOSPlatformSetup
{
    public static void Init()
    {
        // Redirect wgpu DllImport calls to statically linked symbols.
        // On iOS, wgpu-native is linked as a static library, so all
        // symbols are in the main program.
        var mainProgram = NativeLibrary.GetMainProgramHandle();
        NativeLibrary.SetDllImportResolver(typeof(WebGPUGraphicsDriver).Assembly, (name, assembly, path) =>
        {
            if (name.Contains("wgpu"))
                return mainProgram;
            return nint.Zero;
        });
        NativeLibrary.SetDllImportResolver(typeof(Silk.NET.WebGPU.WebGPU).Assembly, (name, assembly, path) =>
        {
            if (name.Contains("wgpu"))
                return mainProgram;
            return nint.Zero;
        });

        // Resolve SDL3 from the embedded framework bundle.
        // The DllImport lives in SDL3-CS.dll, not NoZ.Desktop.dll.
        NativeLibrary.SetDllImportResolver(typeof(SDL.SDL3).Assembly, (name, assembly, path) =>
        {
            if (name == "SDL3")
            {
                if (NativeLibrary.TryLoad("@rpath/SDL3.framework/SDL3", out var handle))
                    return handle;
            }
            return nint.Zero;
        });

        // Tell SDL we're managing our own entry point (no SDL_main).
        SDL_SetMainReady();

        // Use CADisplayLink for frame callbacks on iOS.
        // SDL_SetiOSAnimationCallback triggers a conflicting SDL_RunApp crash
        // when UIApplication.Main is used from .NET.
        SDLPlatform.SetupDisplayLink = callback =>
        {
            var displayLink = CADisplayLink.Create(callback);
            displayLink.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Default);
        };
    }
}
