# Custom Profiler System

## Context

We need a profiler to instrument hotspots in the NoZ engine and game, then visualize frame-by-frame timing data in a standalone viewer app. The system has two halves: engine-side instrumentation (markers, counters, UDP streaming) and a standalone profiler viewer app.

---

## Part 1: Engine Instrumentation (`noz/engine/src/Profiler/`)

### Files to Create

| File | Purpose |
|------|---------|
| `Profiler.cs` | Static class: marker/counter registration, frame buffers, BeginFrame/EndFrame |
| `ProfilerMarker.cs` | `ProfilerMarker` struct + `AutoMarker` dispose struct |
| `ProfilerCounter.cs` | `ProfilerCounter` struct |
| `ProfilerClient.cs` | Static class: UDP sender, packet building |
| `ProfilerPacket.cs` | Packet format constants and serialization helpers |

### Files to Modify

| File | Change |
|------|--------|
| `noz/engine/src/Application.cs` | Add `Profiler.Init()` in `Init()`, `Profiler.BeginFrame()` after `Time.Update()`, `Profiler.EndFrame()` after `Graphics.EndFrame()`, `Profiler.Shutdown()` in `Shutdown()` |

### API Design

**ProfilerMarker** — declared as static fields, marks timed code blocks:
```csharp
public struct ProfilerMarker
{
    public ProfilerMarker(string name);
    public void Begin();
    public void End();
    public AutoMarker Auto();  // using var _ = marker.Auto();
}

public readonly struct AutoMarker(ushort id) : IDisposable
{
    public void Dispose();  // calls Profiler.EndMarker
}
```

**ProfilerCounter** — declared as static fields, tracks per-frame values:
```csharp
public struct ProfilerCounter
{
    public ProfilerCounter(string name);
    public float Value { get; set; }
    public void Increment(float amount = 1f);
}
```

**Usage example:**
```csharp
public static partial class Graphics
{
    private static readonly ProfilerMarker s_markerEndFrame = new("Graphics.EndFrame");
    private static readonly ProfilerCounter s_drawCalls = new("DrawCalls");

    public static void EndFrame()
    {
        using var _ = s_markerEndFrame.Auto();
        // ...
        s_drawCalls.Increment();
    }
}
```

### Profiler Static Class Internals

- Registration: `RegisterMarker(name)` / `RegisterCounter(name)` return `ushort` IDs, use `lock` (infrequent, static init only)
- Frame buffer: fixed array of `MarkerResult` structs (capacity 256), reset each frame
- Counter values: flat `float[]` indexed by ID, reset to 0 at `BeginFrame()`
- Nesting: depth counter + stack of `MarkerStackEntry` (capacity 32) tracking ID + start timestamp
- `Enabled` static bool — when false, Begin/End/Increment early-return (single branch)
- Uses `Stopwatch.GetTimestamp()` for high-res timing (same as `Time.cs`)

### UDP Protocol

**Port:** 9271 (arbitrary, outside well-known ranges)

**Registration packet** (0x01) — sent when new markers/counters are registered:
```
[0]     byte    PacketType = 0x01
[1..2]  ushort  MarkerCount
[3..4]  ushort  CounterCount
Per marker: ushort Id, byte NameLength, byte[] NameUtf8
Per counter: ushort Id, byte NameLength, byte[] NameUtf8
```

**Frame data packet** (0x02) — sent every frame:
```
[0]     byte    PacketType = 0x02
[1..4]  int     FrameNumber
[5..8]  float   DeltaTime
[9..10] ushort  MarkerCount
[11..12] ushort CounterCount
Per marker (11 bytes): ushort Id, long ElapsedTicks, byte Depth
Per counter (6 bytes): ushort Id, float Value
```

Typical frame (40 markers + 20 counters) = ~573 bytes, well under 1472 MTU.

**ProfilerClient:** Uses `System.Net.Sockets.UdpClient` (not the engine's WebSocket driver). Pre-allocated 1472-byte send buffer. Fire-and-forget — no handshake, silently dropped if nobody listening. Checks `_registrationVersion` each frame, re-sends registration packet if changed.

---

## Part 2: Profiler Viewer App (`noz/profiler/`)

### Project Structure (mirrors `noz/editor/`)

```
noz/profiler/
  NoZ.Profiler.csproj               # library
  src/
    ProfilerApplication.cs          # static class + IApplication impl
    ProfilerServer.cs               # UDP receiver, background thread
    ProfilerUI.cs                   # main UI: frame graph + detail panel
    ProfilerStyle.cs                # UI style constants
    FrameRingBuffer.cs              # ring buffer (300 frames = ~5s at 60fps)
    FrameData.cs                    # FrameData, MarkerResult, CounterResult structs
  program/
    NoZ.Profiler.Program.csproj     # WinExe entry point
    Program.cs                      # ProfilerApplication.Run(args)
```

### NoZ.Profiler.csproj References
- `noz/engine/NoZ.csproj`
- `noz/platform/desktop/NoZ.Desktop.csproj`
- `noz/platform/webgpu/NoZ.WebGPU.csproj`
- `noz/generators/NoZ.Generators.csproj` (analyzer)

### ProfilerServer
- Background thread runs `UdpClient.Receive()` loop, enqueues raw `byte[]` into `ConcurrentQueue`
- Main thread drains queue in `Update()`, parses packets, writes into `FrameRingBuffer`
- Maintains marker/counter name dictionaries from registration packets

### Ring Buffer
- Fixed 300-frame capacity
- `FrameData` struct per slot with pre-allocated marker/counter arrays
- Circular write, oldest frames overwritten

### UI Layout
- **Toolbar** (top): Pause/Resume button, connection status, frame count
- **Frame Graph** (middle): Horizontal bar chart of frame times
  - Each bar = 1 frame, height proportional to delta time
  - Color: green < 16.67ms, yellow < 33.33ms, red > 33.33ms
  - Click bar to select frame, auto-scrolls when not paused
- **Detail Panel** (bottom): Selected frame info
  - Frame number, delta time, FPS
  - Markers sorted by elapsed time descending, indented by depth, showing ms + % of frame
  - Counters with name and value

### Entry Point
```csharp
Application.Init(new ApplicationConfig {
    Title = "NoZ Profiler",
    Width = 1200, Height = 700,
    Platform = new SDLPlatform(),
    AudioBackend = new SDLAudioDriver(),
    Vtable = new ProfilerAppVtable(),
    Graphics = new GraphicsConfig { Driver = new WebGPUGraphicsDriver() },
});
Application.Run();
Application.Shutdown();
```

---

## Implementation Order

1. **Engine: Core types** — `Profiler.cs`, `ProfilerMarker.cs`, `ProfilerCounter.cs`
2. **Engine: UDP** — `ProfilerPacket.cs`, `ProfilerClient.cs`
3. **Engine: Integration** — Modify `Application.cs` (Init/BeginFrame/EndFrame/Shutdown)
4. **Viewer: Scaffold** — csproj files, `Program.cs`, `ProfilerApplication.cs`
5. **Viewer: Data** — `FrameData.cs`, `FrameRingBuffer.cs`, `ProfilerServer.cs`
6. **Viewer: UI** — `ProfilerStyle.cs`, `ProfilerUI.cs`
7. **Solution** — Add projects to `Stope.sln` (or a separate noz solution)
8. **Instrument** — Add a few markers/counters to engine subsystems (Graphics, UI, VfxSystem) as proof of concept

## Verification

1. Build engine: `dotnet build noz/engine/NoZ.csproj`
2. Build profiler: `dotnet build noz/profiler/program/NoZ.Profiler.Program.csproj`
3. Build game: `dotnet build game/Stope.csproj`
4. Run profiler viewer, then run game — viewer should show frame graph with timing data
5. Click a frame bar in the viewer — should show marker breakdown and counter values

## Design Decisions (self-resolved)

- **UDP vs WebSocket**: Raw UDP via `System.Net.Sockets.UdpClient` — simpler, fire-and-forget, no connection management. Engine's WebSocket driver is overkill.
- **One packet per frame**: A single packet contains all marker/counter data. Typical frames fit well under MTU. Simpler than per-marker packets.
- **Registration packet**: Sent separately to avoid repeating string names every frame. Re-sent when new markers/counters appear.
- **Ring buffer size**: 300 frames (~5s at 60fps). Enough to scroll back and inspect without excessive memory.
- **Counter reset**: Auto-reset to 0 at frame start. Counters accumulate within a frame via `Increment()`. All registered counters are sent every frame.
- **Thread safety**: Registration uses lock (rare). Per-frame writes are single-threaded (game loop). Viewer uses ConcurrentQueue to bridge receive thread → main thread.
- **Profiler.Enabled**: Defaults to false. When disabled, all instrumentation is a single branch check — near-zero overhead.
