//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Buffers.Binary;
using System.Text;

namespace NoZ;

public static class ProfilerPacket
{
    public const byte TypeFrameHeader = 0x01;
    public const byte TypeMarker = 0x02;
    public const byte TypeCounter = 0x03;

    // Frame header: type(1) + frameNumber(4) + deltaTime(4) = 9 bytes
    public static int WriteFrameHeader(Span<byte> buffer, int frameNumber, float deltaTime)
    {
        buffer[0] = TypeFrameHeader;
        BinaryPrimitives.WriteInt32LittleEndian(buffer[1..], frameNumber);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[5..], deltaTime);
        return 9;
    }

    // Marker: type(1) + ticks(8) + depth(1) + callCount(2) + allocBytes(8) + nameLen(1) + name(N)
    public static int WriteMarker(Span<byte> buffer, long elapsedTicks, byte depth, int callCount, long allocBytes, string name)
    {
        buffer[0] = TypeMarker;
        BinaryPrimitives.WriteInt64LittleEndian(buffer[1..], elapsedTicks);
        buffer[9] = depth;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[10..], (ushort)callCount);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[12..], allocBytes);
        var nameLen = Encoding.UTF8.GetBytes(name, buffer[21..]);
        buffer[20] = (byte)nameLen;
        return 21 + nameLen;
    }

    // Counter: type(1) + value(4) + nameLen(1) + name(N)
    public static int WriteCounter(Span<byte> buffer, float value, string name)
    {
        buffer[0] = TypeCounter;
        BinaryPrimitives.WriteSingleLittleEndian(buffer[1..], value);
        var nameLen = Encoding.UTF8.GetBytes(name, buffer[6..]);
        buffer[5] = (byte)nameLen;
        return 6 + nameLen;
    }

    // --- Readers ---

    public static void ReadFrameHeader(ReadOnlySpan<byte> data, out int frameNumber, out float deltaTime)
    {
        frameNumber = BinaryPrimitives.ReadInt32LittleEndian(data[1..]);
        deltaTime = BinaryPrimitives.ReadSingleLittleEndian(data[5..]);
    }

    public static void ReadMarker(ReadOnlySpan<byte> data, out long elapsedTicks, out byte depth, out int callCount, out long allocBytes, out string name)
    {
        elapsedTicks = BinaryPrimitives.ReadInt64LittleEndian(data[1..]);
        depth = data[9];
        callCount = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
        allocBytes = BinaryPrimitives.ReadInt64LittleEndian(data[12..]);
        var nameLen = data[20];
        name = Encoding.UTF8.GetString(data.Slice(21, nameLen));
    }

    public static void ReadCounter(ReadOnlySpan<byte> data, out float value, out string name)
    {
        value = BinaryPrimitives.ReadSingleLittleEndian(data[1..]);
        var nameLen = data[5];
        name = Encoding.UTF8.GetString(data.Slice(6, nameLen));
    }
}
