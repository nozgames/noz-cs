//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class SpriteFrame : IDisposable
{
    public readonly Shape Shape = new();
    public int Hold;

    public void Dispose()
    {
        Shape.Dispose();
    }
}

public partial class SpriteDocument
{
    public int TotalTimeSlots
    {
        get
        {
            var total = 0;
            for (var i = 0; i < FrameCount; i++)
                total += 1 + Frames[i].Hold;
            return total;
        }
    }

    public bool ShouldShowInSkeleton(SkeletonDocument skeleton) => ShowInSkeleton && Skeleton == skeleton;

    public static int GetFrameAtTimeSlot(SpriteFrame[] frames, ushort frameCount, int globalTimeSlot)
    {
        var accumulated = 0;
        for (var f = 0; f < frameCount; f++)
        {
            var slots = 1 + frames[f].Hold;
            if (accumulated + slots > globalTimeSlot)
                return f;
            accumulated += slots;
        }
        return frameCount - 1;
    }

    public int GetFrameAtTimeSlot(int globalTimeSlot) =>
        GetFrameAtTimeSlot(Frames, FrameCount, globalTimeSlot);

    public int InsertFrame(int insertAt)
    {
        if (FrameCount >= Sprite.MaxFrames)
            return -1;

        FrameCount++;
        var copyFrame = Math.Max(0, insertAt - 1);

        for (var i = FrameCount - 1; i > insertAt; i--)
        {
            Frames[i].Shape.CopyFrom(Frames[i - 1].Shape);
            Frames[i].Hold = Frames[i - 1].Hold;
        }

        if (copyFrame >= 0 && copyFrame < FrameCount)
            Frames[insertAt].Shape.CopyFrom(Frames[copyFrame].Shape);

        Frames[insertAt].Hold = 0;
        return insertAt;
    }

    public int DeleteFrame(int frameIndex)
    {
        if (FrameCount <= 1)
            return frameIndex;

        for (var i = frameIndex; i < FrameCount - 1; i++)
        {
            Frames[i].Shape.CopyFrom(Frames[i + 1].Shape);
            Frames[i].Hold = Frames[i + 1].Hold;
        }

        Frames[FrameCount - 1].Shape.Clear();
        Frames[FrameCount - 1].Hold = 0;
        FrameCount--;
        return Math.Min(frameIndex, FrameCount - 1);
    }
}
