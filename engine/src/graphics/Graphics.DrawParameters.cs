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
    private static NativeArray<byte> _drawParameterData;
    private static NativeArray<int> _drawParameterLengths;
    private static GraphicsSnapshotIndex _drawParameterIndex;
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

        var hash = GraphicsSnapshotIndex.Hash(data);
        for (var i = _drawParameterIndex.First(hash); i >= 0; i = _drawParameterIndex.Next(i))
        {
            if (!data.SequenceEqual(GetDrawParameters(i))) continue;
            SetDrawParameterIndex(i + 1);
            return;
        }

        if (_drawParameterCount >= _maxGlobalSnapshots)
            throw new InvalidOperationException(
                $"Draw parameter snapshot budget exhausted ({_maxGlobalSnapshots}). " +
                $"Increase {nameof(GraphicsConfig)}.{nameof(GraphicsConfig.MaxGlobalSnapshots)}.");

        data.CopyTo(_drawParameterData.AsSpan(_drawParameterCount * MaxDrawParameterBytes, data.Length));
        _drawParameterLengths[_drawParameterCount] = data.Length;
        _drawParameterIndex.Add(hash, _drawParameterCount);
        SetDrawParameterIndex(++_drawParameterCount);
    }

    private static ReadOnlySpan<byte> GetDrawParameters(int index) =>
        _drawParameterData.AsReadonlySpan(index * MaxDrawParameterBytes, _drawParameterLengths[index]);

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
