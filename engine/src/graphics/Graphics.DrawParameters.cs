//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;

namespace NoZ;

public static partial class Graphics
{
    /// <summary>Maximum size of optional, caller-defined per-draw shader parameters.</summary>
    public const int MaxDrawParameterBytes = 256;
    private const int GlobalsPrefixBytes = 80;
    private static readonly List<byte[]> _drawParameterSnapshots = [];
    private static int _drawParameterCount;

    /// <summary>
    /// Copies parameters for subsequent draws, including deferred/sorted draws.
    /// The shader declares this data after projection and time in its globals
    /// block, starting at byte 80. Supply the shader's layout and 16-byte padding.
    /// Parameters participate in PushState/PopState and reset each frame.
    /// </summary>
    public static void SetDrawParameters(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            ClearDrawParameters();
            return;
        }
        if (data.Length > MaxDrawParameterBytes || (data.Length & 15) != 0)
            throw new ArgumentException(
                $"Draw parameters must be a multiple of 16 bytes, at most {MaxDrawParameterBytes} bytes.", nameof(data));

        for (var i = 0; i < _drawParameterCount; i++)
        {
            if (!data.SequenceEqual(_drawParameterSnapshots[i])) continue;
            SetDrawParameterIndex(i + 1);
            return;
        }

        if (_drawParameterCount >= _maxGlobalSnapshots)
            throw new InvalidOperationException(
                $"Draw parameter snapshot budget exhausted ({_maxGlobalSnapshots}). " +
                $"Increase {nameof(GraphicsConfig)}.{nameof(GraphicsConfig.MaxGlobalSnapshots)}.");

        if (_drawParameterCount == _drawParameterSnapshots.Count)
            _drawParameterSnapshots.Add(new byte[data.Length]);
        else if (_drawParameterSnapshots[_drawParameterCount].Length != data.Length)
            _drawParameterSnapshots[_drawParameterCount] = new byte[data.Length];

        data.CopyTo(_drawParameterSnapshots[_drawParameterCount]);
        SetDrawParameterIndex(++_drawParameterCount);
    }

    public static void SetDrawParameters<T>(in T data) where T : unmanaged =>
        SetDrawParameters(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in data, 1)));

    public static void ClearDrawParameters() => SetDrawParameterIndex(0);

    private static void SetDrawParameterIndex(int index)
    {
        if (CurrentState.DrawParameterIndex == index) return;
        CurrentState.DrawParameterIndex = index;
        _batchStateDirty = true;
    }
}
