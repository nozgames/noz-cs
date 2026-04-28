//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public interface IProjectSync : IDisposable
{
    string Name { get; }
    string ProjectPath { get; }
    bool IsSyncing { get; }
    Task SyncAsync(CancellationToken ct = default);
    event Action? SyncCompleted;
}
