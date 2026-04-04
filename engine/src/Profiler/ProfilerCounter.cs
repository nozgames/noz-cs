//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public readonly struct ProfilerCounter(string name)
{
    private readonly ushort _id = Profiler.RegisterCounter(name);

    public float Value
    {
        get => Profiler.GetCounterValue(_id);
        set => Profiler.SetCounterValue(_id, value);
    }

    public void Increment(float amount = 1f) => Profiler.IncrementCounter(_id, amount);
}
