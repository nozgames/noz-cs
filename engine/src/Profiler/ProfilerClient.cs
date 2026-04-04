//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Net.Sockets;

namespace NoZ;

public static class ProfilerClient
{
    public const int Port = 9271;
    private const int BufferSize = 65536;

    private static UdpClient? _client;
    private static Thread? _sendThread;
    private static volatile bool _running;
    private static volatile bool _sending;
    private static readonly byte[] _buffer = new byte[BufferSize];
    private static readonly int[] _packetOffsets = new int[512];
    private static readonly int[] _packetLengths = new int[512];
    private static int _packetCount;
    private static int _bufferPos;

    internal static void SendFrame()
    {
        EnsureStarted();
        if (_sending) return;

        _packetCount = 0;
        _bufferPos = 0;

        Write(ProfilerPacket.WriteFrameHeader(_buffer.AsSpan(_bufferPos), Profiler.FrameNumber, Time.UnscaledDeltaTime));

        for (var i = 0; i < Profiler.MarkerOrderCount; i++)
        {
            var id = Profiler.GetMarkerOrderId(i);
            ref readonly var m = ref Profiler.GetMarker(id);
            Write(ProfilerPacket.WriteMarker(_buffer.AsSpan(_bufferPos), m.ElapsedTicks, m.Depth, m.CallCount, m.AllocBytes, m.Name));
        }

        for (ushort i = 0; i < Profiler.CounterCount; i++)
        {
            ref readonly var c = ref Profiler.GetCounter(i);
            Write(ProfilerPacket.WriteCounter(_buffer.AsSpan(_bufferPos), c.Value, c.Name));
        }

        _sending = true;
    }

    private static void Write(int len)
    {
        if (_packetCount >= _packetOffsets.Length || _bufferPos + len > BufferSize) return;
        _packetOffsets[_packetCount] = _bufferPos;
        _packetLengths[_packetCount] = len;
        _packetCount++;
        _bufferPos += len;
    }

    private static void EnsureStarted()
    {
        if (_sendThread != null) return;

        try
        {
            _client = new UdpClient();
            _client.Connect("127.0.0.1", Port);
        }
        catch (Exception ex)
        {
            Log.Warning($"Profiler client failed to connect: {ex.Message}");
            _client = null;
            return;
        }

        _running = true;
        _sendThread = new Thread(SendLoop)
        {
            Name = "ProfilerClient",
            IsBackground = true
        };
        _sendThread.Start();
    }

    private static void SendLoop()
    {
        while (_running)
        {
            if (_sending)
            {
                for (var i = 0; i < _packetCount; i++)
                {
                    try { _client?.Send(_buffer.AsSpan(_packetOffsets[i], _packetLengths[i])); }
                    catch { }
                }
                _sending = false;
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    internal static void Shutdown()
    {
        _running = false;
        _sendThread?.Join(1000);
        _sendThread = null;
        _client?.Close();
        _client = null;
    }
}
