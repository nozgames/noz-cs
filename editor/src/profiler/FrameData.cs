//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public struct ProfilerMarkerResult
{
    public string Name;
    public long ElapsedTicks;
    public byte Depth;
    public int CallCount;
    public long AllocBytes;
}

public struct ProfilerCounterResult
{
    public string Name;
    public float Value;
}

public class FrameData
{
    public int FrameNumber;
    public float DeltaTime;
    public ProfilerMarkerResult[] Markers = new ProfilerMarkerResult[256];
    public int MarkerCount;
    public ProfilerCounterResult[] Counters = new ProfilerCounterResult[64];
    public int CounterCount;

    public void Clear()
    {
        FrameNumber = 0;
        DeltaTime = 0;
        MarkerCount = 0;
        CounterCount = 0;
    }
}
