//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace NoZ.Editor;

public class ProfilerServer
{
    private UdpClient? _client;
    private Thread? _receiveThread;
    private volatile bool _running;
    private readonly ConcurrentQueue<byte[]> _packetQueue = new();
    private readonly FrameRingBuffer _buffer = new();

    private FrameData? _currentFrame;

    public FrameRingBuffer Buffer => _buffer;
    public bool Connected { get; private set; }

    public void Start()
    {
        try
        {
            _client = new UdpClient(ProfilerClient.Port);
            _running = true;
            _receiveThread = new Thread(ReceiveLoop)
            {
                Name = "ProfilerServer",
                IsBackground = true
            };
            _receiveThread.Start();
            Log.Info($"Profiler server listening on port {ProfilerClient.Port}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to start profiler server: {ex.Message}");
        }
    }

    public void Stop()
    {
        _running = false;
        _client?.Close();
        _client = null;
        _receiveThread?.Join(1000);
        _receiveThread = null;
    }

    public void Update()
    {
        var received = false;
        while (_packetQueue.TryDequeue(out var packet))
        {
            received = true;
            ProcessPacket(packet);
        }

        if (received) Connected = true;
    }

    public void Drain()
    {
        while (_packetQueue.TryDequeue(out _)) { }
        _currentFrame = null;
    }

    private void ReceiveLoop()
    {
        var remote = new IPEndPoint(IPAddress.Any, 0);

        while (_running)
        {
            try
            {
                var data = _client!.Receive(ref remote);
                _packetQueue.Enqueue(data);
            }
            catch (SocketException) when (!_running)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning($"Profiler receive error: {ex.Message}");
            }
        }
    }

    private void ProcessPacket(byte[] data)
    {
        if (data.Length == 0) return;

        switch (data[0])
        {
            case ProfilerPacket.TypeFrameHeader:
                CommitCurrentFrame();
                ProfilerPacket.ReadFrameHeader(data, out var frameNumber, out var deltaTime);
                _currentFrame = _buffer.GetWriteSlot();
                _currentFrame.FrameNumber = frameNumber;
                _currentFrame.DeltaTime = deltaTime;
                break;

            case ProfilerPacket.TypeMarker:
                if (_currentFrame != null && _currentFrame.MarkerCount < _currentFrame.Markers.Length)
                {
                    ProfilerPacket.ReadMarker(data, out var ticks, out var depth, out var callCount, out var allocBytes, out var name);
                    ref var m = ref _currentFrame.Markers[_currentFrame.MarkerCount++];
                    m.Name = name;
                    m.ElapsedTicks = ticks;
                    m.Depth = depth;
                    m.CallCount = callCount;
                    m.AllocBytes = allocBytes;
                }
                break;

            case ProfilerPacket.TypeCounter:
                if (_currentFrame != null && _currentFrame.CounterCount < _currentFrame.Counters.Length)
                {
                    ProfilerPacket.ReadCounter(data, out var value, out var cname);
                    ref var c = ref _currentFrame.Counters[_currentFrame.CounterCount++];
                    c.Name = cname;
                    c.Value = value;
                }
                break;
        }
    }

    private void CommitCurrentFrame()
    {
        if (_currentFrame == null) return;
        _buffer.CommitWrite();
        _currentFrame = null;
    }
}
