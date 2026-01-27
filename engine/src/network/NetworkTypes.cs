//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Platform;

public enum ConnectionState : byte
{
    Disconnected,
    Connecting,
    Connected,
    Failed
}

public readonly struct NetworkMessage
{
    public readonly byte[] Data;
    public readonly int Length;

    public NetworkMessage(byte[] data, int length)
    {
        Data = data;
        Length = length;
    }

    public ReadOnlySpan<byte> AsSpan() => Data.AsSpan(0, Length);
}
