# iOS Build Guide

## Prerequisites (macOS only)

1. **Xcode** — install from App Store or developer.apple.com
2. **.NET 10 SDK** — install from dot.net
3. **iOS workload** — `dotnet workload install ios`

## Step 1: Download wgpu-native for iOS

Prebuilt binaries are available from the wgpu-native GitHub releases:

```bash
# Download the iOS release build
curl -L https://github.com/gfx-rs/wgpu-native/releases/download/v27.0.4.0/wgpu-ios-aarch64-release.zip -o wgpu-ios.zip

# Extract and copy into the project
unzip wgpu-ios.zip -d wgpu-ios
mkdir -p platform/ios/libs
cp wgpu-ios/lib/libwgpu_native.a platform/ios/libs/
```

For simulator builds (optional):
```bash
curl -L https://github.com/gfx-rs/wgpu-native/releases/download/v27.0.4.0/wgpu-ios-aarch64-simulator-release.zip -o wgpu-ios-sim.zip
```

Check for newer releases at: https://github.com/gfx-rs/wgpu-native/releases

## Step 2: Enable the NativeReference

In `dominoz.iOS.csproj`, uncomment the `NativeReference` section:

```xml
<ItemGroup>
  <NativeReference Include="libs\libwgpu_native.a">
    <Kind>Static</Kind>
    <ForceLoad>true</ForceLoad>
  </NativeReference>
</ItemGroup>
```

## Step 3: Build

```bash
cd /path/to/dominoz

# Debug build
dotnet build platform/ios/dominoz.iOS.csproj

# Release build (AOT)
dotnet publish platform/ios/dominoz.iOS.csproj -c Release
```

## Step 4: Deploy

```bash
# Deploy to connected device
dotnet run --project platform/ios/dominoz.iOS.csproj

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
