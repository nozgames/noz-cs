//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using NoZ.Platform.WebGPU;

namespace NoZ.Platform;

public static class iOSPlatformSetup
{
    public static void Init()
    {
        // Redirect Silk.NET wgpu DllImport calls to statically linked symbols.
        // On iOS, dynamic libraries are not allowed — wgpu-native is linked as
        // a static library, so all symbols are in the main program.
        NativeLibrary.SetDllImportResolver(typeof(WebGPUGraphicsDriver).Assembly, (name, assembly, path) =>
        {
            if (name.Contains("wgpu"))
                return NativeLibrary.GetMainProgramHandle();
            return nint.Zero;
        });
    }
}
