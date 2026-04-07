# iOS Build Guide

## Prerequisites (macOS only)

1. **Xcode** — install from App Store or developer.apple.com
2. **.NET 10 SDK** — install from dot.net
3. **iOS workload** — `dotnet workload install ios`

## Build

```bash
# Debug build (simulator)
dotnet build platform/ios/Stope.iOS.csproj

# Release build (AOT, device)
dotnet publish platform/ios/Stope.iOS.csproj -c Release
```

## Deploy

```bash
# Deploy to connected device
dotnet run --project platform/ios/Stope.iOS.csproj

# Or use Xcode/ios-deploy for more control
```

## Troubleshooting

### Static linking issues with Silk.NET
If Silk.NET's DllImport resolution fails, the `NativeLibrary.SetDllImportResolver` in `Program.cs` redirects wgpu lookups to the statically linked symbols. If this doesn't work, you may need to also add a resolver for the Silk.NET.WebGPU assembly:

```csharp
NativeLibrary.SetDllImportResolver(typeof(Silk.NET.WebGPU.WebGPU).Assembly, ...);
```

### SDL3 framework
The `ppy.SDL3-CS` NuGet package includes iOS binaries at `runtimes/ios/native/SDL3.xcframework/`. These should be picked up automatically by the build system.
