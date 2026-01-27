//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Platform;

public interface INetworkDriver
{
    void Init();
    void Shutdown();

    // Client operations
    void Connect(string url);
    void Disconnect();
    ConnectionState GetConnectionState();
    void Send(ReadOnlySpan<byte> data);
    bool TryReceive(out NetworkMessage message);

    // Server operations (desktop only)
    bool SupportsServer { get; }
    void StartServer(int port, int maxConnections);
    void StopServer();
    bool IsServerRunning { get; }
    void Broadcast(ReadOnlySpan<byte> data);
    void SendTo(int connectionId, ReadOnlySpan<byte> data);
    bool TryReceiveServer(out int connectionId, out NetworkMessage message);

    // Events
    event Action? OnConnected;
    event Action? OnDisconnected;
    event Action<int>? OnClientConnected;
    event Action<int>? OnClientDisconnected;
}
