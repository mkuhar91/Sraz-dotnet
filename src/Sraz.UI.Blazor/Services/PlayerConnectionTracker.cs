using Sraz.Application.Rooms;

namespace Sraz.UI.Blazor.Services;

/// <summary>Counts live connections across all circuits belonging to the same cookie identity.</summary>
public sealed class PlayerConnectionTracker(IRoomManager rooms, TimeProvider time) : BackgroundService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, HashSet<string>> _connections = new();
    private readonly HashSet<Guid> _pendingPlayers = new();

    public async Task SetConnectedAsync(Guid playerId, string circuitId, bool connected)
    {
        await _gate.WaitAsync();
        try
        {
            _connections.TryGetValue(playerId, out var circuits);
            var wasConnected = circuits is { Count: > 0 };
            if (connected)
            {
                if (circuits is null)
                {
                    circuits = new HashSet<string>(StringComparer.Ordinal);
                    _connections.Add(playerId, circuits);
                }

                circuits.Add(circuitId);
            }
            else if (circuits is not null)
            {
                circuits.Remove(circuitId);
                if (circuits.Count == 0)
                {
                    _connections.Remove(playerId);
                }
            }

            var isConnected = circuits is { Count: > 0 };
            if (wasConnected == isConnected && !_pendingPlayers.Contains(playerId))
            {
                return;
            }

            await UpdateRoomsAsync(playerId, isConnected, CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await _gate.WaitAsync(stoppingToken);
            try
            {
                foreach (var playerId in _pendingPlayers.ToArray())
                {
                    await UpdateRoomsAsync(playerId, _connections.ContainsKey(playerId), stoppingToken);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private async Task UpdateRoomsAsync(Guid playerId, bool connected, CancellationToken cancellationToken)
    {
        var failed = false;
        foreach (var room in rooms.GetRooms())
        {
            var result = await room.SetConnectedAsync(playerId, connected, cancellationToken);
            failed |= result.Error == RoomError.PersistenceFailed;
        }

        if (failed)
        {
            _pendingPlayers.Add(playerId);
        }
        else
        {
            _pendingPlayers.Remove(playerId);
        }
    }
}
