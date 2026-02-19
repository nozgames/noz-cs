//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

public enum Easing
{
    None,
    QuadIn,
    QuadOut,
    QuadInOut,
    CubicIn,
    CubicOut,
    CubicInOut,
    BackIn,
    BackOut,
    BackInOut,
    ElasticOut,
}

public enum TweenMode
{
    Once,
    Loop,
    PingPong,
    PingPongLoop
}

public struct Tween
{
    private const int MaxTweens = 256;

    private struct TweenData
    {
        public float Elapsed;
        public float Duration;
        public float Delay;
        public Easing Easing;
        public TweenMode Mode;
        public bool Forward;
        public ushort Generation;
        public int LastUpdateFrame;
    }

    private static TweenData[] _data = new TweenData[MaxTweens];

    internal ushort Index;
    internal ushort Generation;

    public readonly float NormalizedTime
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!IsValid()) return 1f;
            ref var d = ref _data[Index];
            return MathEx.Clamp01((d.Elapsed - d.Delay) / d.Duration);
        }
    }

    public readonly bool IsComplete
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!IsValid()) return true;
            ref var d = ref _data[Index];
            return (d.Mode == TweenMode.Once || d.Mode == TweenMode.PingPong) && d.Elapsed >= d.Delay + d.Duration;
        }
    }

    internal Tween(ushort index, ushort generation)
    {
        Index = index;
        Generation = generation;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Update(float from, float to, float defaultNormalizedTime = 1f)
    {
        Advance();
        if (!IsValid())
            return from + (to - from) * defaultNormalizedTime;

        ref var d = ref _data[Index];
        var rawT = NormalizedTime;

        // Handle pingpong: 0→0.5→1 becomes 0→1→0
        float interpT;
        if (d.Mode == TweenMode.PingPong || d.Mode == TweenMode.PingPongLoop)
        {
            if (rawT < 0.5f)
                interpT = MathEx.Ease(d.Easing, rawT * 2f);
            else
                interpT = MathEx.Ease(d.Easing, (1f - rawT) * 2f);
        }
        else
        {
            interpT = MathEx.Ease(d.Easing, rawT);
        }

        return from + (to - from) * interpT;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 Update(Vector2 from, Vector2 to, float defaultNormalizedTime = 1f)
    {
        Advance();
        if (!IsValid())
            return from + (to - from) * defaultNormalizedTime;

        ref var d = ref _data[Index];
        var rawT = NormalizedTime;

        // Handle pingpong: 0→0.5→1 becomes 0→1→0
        float interpT;
        if (d.Mode == TweenMode.PingPong || d.Mode == TweenMode.PingPongLoop)
        {
            if (rawT < 0.5f)
                interpT = MathEx.Ease(d.Easing, rawT * 2f);
            else
                interpT = MathEx.Ease(d.Easing, (1f - rawT) * 2f);
        }
        else
        {
            interpT = MathEx.Ease(d.Easing, rawT);
        }

        return from + (to - from) * interpT;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Color Update(Color from, Color to, float defaultNormalizedTime = 1f)
    {
        Advance();
        if (!IsValid())
            return Color.Mix(from, to, defaultNormalizedTime);

        ref var d = ref _data[Index];
        var rawT = NormalizedTime;

        // Handle pingpong: 0→0.5→1 becomes 0→1→0
        float interpT;
        if (d.Mode == TweenMode.PingPong || d.Mode == TweenMode.PingPongLoop)
        {
            if (rawT < 0.5f)
                interpT = MathEx.Ease(d.Easing, rawT * 2f);
            else
                interpT = MathEx.Ease(d.Easing, (1f - rawT) * 2f);
        }
        else
        {
            interpT = MathEx.Ease(d.Easing, rawT);
        }

        return Color.Mix(from, to, interpT);
    }

    public readonly void Restart()
    {
        if (!IsValid()) return;
        _data[Index].Elapsed = 0f;
        _data[Index].Forward = true;
    }

    private readonly bool IsValid() =>
        _data[Index].Generation == Generation && _data[Index].Duration > 0;

    private readonly void Advance()
    {
        if (!IsValid())
            return;

        ref var d = ref _data[Index];
        d.Elapsed += Time.DeltaTime;
        d.LastUpdateFrame = Time.FrameCount;

        if (d.Elapsed < d.Delay + d.Duration)
            return;

        if (d.Mode == TweenMode.Loop || d.Mode == TweenMode.PingPongLoop)
        {
            d.Elapsed = d.Delay;
        }
        else
        {
            // Once or PingPong - stop the tween
            d.Duration = 0;
        }
    }

    private static int AllocateSlot()
    {
        for (int i = 0; i < MaxTweens; i++)
        {
            ref var data = ref _data[i];
            if (data.Duration <= 0)
                return i;

            if (Time.FrameCount - data.LastUpdateFrame > 1)
                return i;
        }

        return -1;
    }

    public static Tween Start(
        float duration,
        float delay = 0f,
        Easing easing = Easing.None,
        TweenMode mode = TweenMode.Once)
    {
        var index = AllocateSlot();
        if (index < 0)
            return new Tween(0, 0);

        ref var d = ref _data[index];
        d.Elapsed = 0f;
        d.Duration = duration;
        d.Delay = delay;
        d.Easing = easing;
        d.Mode = mode;
        d.Forward = true;
        d.Generation++;
        if (d.Generation == 0) d.Generation = 1;
        d.LastUpdateFrame = Time.FrameCount;

        return new Tween { Index = (ushort)index, Generation = d.Generation };
    }
}
