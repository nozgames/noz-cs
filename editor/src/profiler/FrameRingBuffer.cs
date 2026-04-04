//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class FrameRingBuffer
{
    public const int Capacity = 300;

    private readonly FrameData[] _frames;
    private int _writeIndex;
    private int _count;

    public int Count => _count;
    public int NewestIndex => _count > 0 ? (_writeIndex - 1 + Capacity) % Capacity : -1;
    public int OldestIndex => _count > 0 ? (_writeIndex - _count + Capacity) % Capacity : -1;

    public FrameRingBuffer()
    {
        _frames = new FrameData[Capacity];
        for (var i = 0; i < Capacity; i++)
            _frames[i] = new FrameData();
    }

    public FrameData GetWriteSlot()
    {
        var frame = _frames[_writeIndex];
        frame.Clear();
        return frame;
    }

    public void CommitWrite()
    {
        _writeIndex = (_writeIndex + 1) % Capacity;
        if (_count < Capacity)
            _count++;
    }

    public FrameData? Get(int index)
    {
        if (index < 0 || index >= Capacity) return null;
        if (_count == 0) return null;

        var oldest = OldestIndex;
        if (_count < Capacity)
        {
            if (index < oldest || index >= _writeIndex) return null;
        }

        return _frames[index];
    }

    public FrameData? GetByAge(int age)
    {
        if (age < 0 || age >= _count) return null;
        var index = (NewestIndex - age + Capacity) % Capacity;
        return _frames[index];
    }
}
