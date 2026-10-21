using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sraz.Application.Rooms;

namespace Sraz.Infrastructure.Persistence;

/// <summary>Atomically stores the room, snapshot and any completed game results.</summary>
public sealed class EfRoomStore(IServiceScopeFactory scopeFactory) : IRoomStore
{
    public async Task SaveAsync(RoomSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await SaveOnceAsync(snapshot, cancellationToken);
                return;
            }
            catch (Exception exception) when (attempt < 7 && IsConcurrentWrite(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10 * (attempt + 1)), cancellationToken);
            }
        }
    }

    private static bool IsConcurrentWrite(Exception exception) =>
        exception is PostgresException
        {
            SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected
                or PostgresErrorCodes.UniqueViolation
        } ||
        exception.InnerException is { } innerException && IsConcurrentWrite(innerException);

    private async Task SaveOnceAsync(RoomSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
        var postgres = dbContext.Database.IsNpgsql();
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(
                postgres ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, cancellationToken)
            : null;
        if (postgres)
        {
            // Transaction-scoped database locks also serialize writers in other server processes.
            var roomKey = $"room:{snapshot.Code}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({roomKey}, 0))", cancellationToken);
            if (snapshot.State == RoomState.Finished && snapshot.Result is { } gameResult)
            {
                var gameKey = $"game:{gameResult.GameId}";
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({gameKey}, 0))", cancellationToken);
            }
        }

        var json = RoomSnapshotSerializer.Serialize(snapshot);

        var room = await dbContext.GameRooms.FirstOrDefaultAsync(entity => entity.Code == snapshot.Code, cancellationToken);
        if (room is null)
        {
            dbContext.GameRooms.Add(new GameRoomEntity
            {
                Code = snapshot.Code,
                State = snapshot.State.ToString(),
                CreatedAtUtc = snapshot.CreatedAtUtc.UtcDateTime
            });
        }
        else
        {
            room.State = snapshot.State.ToString();
        }

        var entry = await dbContext.GameSnapshots
            .Where(entity => entity.RoomCode == snapshot.Code)
            .OrderByDescending(entity => entity.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (entry is null)
        {
            dbContext.GameSnapshots.Add(new GameSnapshotEntity
            {
                RoomCode = snapshot.Code,
                StateJson = json,
                SchemaVersion = snapshot.SchemaVersion,
                CreatedAtUtc = snapshot.LastActivityUtc.UtcDateTime
            });
        }
        else
        {
            entry.StateJson = json;
            entry.SchemaVersion = snapshot.SchemaVersion;
            entry.CreatedAtUtc = snapshot.LastActivityUtc.UtcDateTime;
        }

        if (snapshot.State == RoomState.Finished && snapshot.Result is { } result)
        {
            if (snapshot.GameId != result.GameId || result.GameId == Guid.Empty)
            {
                throw new InvalidOperationException("A finished result must identify the snapshot's game.");
            }

            var recordedPlayers = await dbContext.LeaderboardEntries
                .Where(entity => entity.GameId == result.GameId)
                .Select(entity => entity.PlayerId)
                .ToListAsync(cancellationToken);
            foreach (var player in result.Players.DistinctBy(player => player.PlayerId))
            {
                if (player.PlayerId == Guid.Empty || recordedPlayers.Contains(player.PlayerId))
                {
                    continue;
                }

                dbContext.LeaderboardEntries.Add(new LeaderboardEntryEntity
                {
                    GameId = result.GameId,
                    PlayerId = player.PlayerId,
                    PlayerName = player.Name,
                    Won = player.Won,
                    Mode = "Room",
                    PlayedAtUtc = result.FinishedAtUtc.UtcDateTime
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<RoomSnapshot>> LoadActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
        var active = new[] { RoomState.Lobby.ToString(), RoomState.InProgress.ToString() };
        var rows = await dbContext.GameRooms
            .AsNoTracking()
            .Where(room => active.Contains(room.State))
            .Join(
                dbContext.GameSnapshots.AsNoTracking(),
                room => room.Code,
                snapshot => snapshot.RoomCode,
                (room, snapshot) => new { snapshot.Id, snapshot.StateJson, snapshot.SchemaVersion })
            .ToListAsync(cancellationToken);

        var result = new List<RoomSnapshot>();
        foreach (var row in rows.OrderBy(row => row.Id))
        {
            if (row.SchemaVersion is < 1 or > RoomSnapshot.CurrentSchemaVersion)
            {
                continue;
            }

            try
            {
                if (RoomSnapshotSerializer.Deserialize(row.StateJson) is { } snapshot)
                {
                    result.Add(snapshot);
                }
            }
            catch (JsonException)
            {
            }
        }

        return result;
    }

    public async Task DeleteAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
        dbContext.GameSnapshots.RemoveRange(await dbContext.GameSnapshots.Where(entity => entity.RoomCode == code).ToListAsync(cancellationToken));
        dbContext.GameRooms.RemoveRange(await dbContext.GameRooms.Where(entity => entity.Code == code).ToListAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
