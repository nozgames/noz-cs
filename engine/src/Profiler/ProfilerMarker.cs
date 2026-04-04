//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public readonly struct ProfilerMarker(string name)
{
    public readonly struct AutoMarker(ushort id) : IDisposable
    {
        public void Dispose() => Profiler.EndMarker(id);
    }

    private readonly ushort _id = Profiler.RegisterMarker(name);

    public AutoMarker Begin() 
    {
        Profiler.BeginMarker(_id);
        return new AutoMarker(_id);
    }

    public void End() => Profiler.EndMarker(_id);
}

