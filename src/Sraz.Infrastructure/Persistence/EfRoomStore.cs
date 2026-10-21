using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sraz.Application.Rooms;

namespace Sraz.Infrastructure.Persistence;

/// <summary>Stores the room row and its latest snapshot with a single SaveChanges, so both are written in one transaction.</summary>
public sealed class EfRoomStore(IServiceScopeFactory scopeFactory) : IRoomStore
{
    public async Task SaveAsync(RoomSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
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

        await dbContext.SaveChangesAsync(cancellationToken);
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
            if (row.SchemaVersion != RoomSnapshot.CurrentSchemaVersion)
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
