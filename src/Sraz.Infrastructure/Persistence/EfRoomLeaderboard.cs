using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sraz.Application.Abstractions;

namespace Sraz.Infrastructure.Persistence;

public sealed class EfRoomLeaderboard(IServiceScopeFactory scopeFactory) : IRoomLeaderboard
{
    public async Task<IReadOnlyList<RoomLeaderboardEntry>> GetTopAsync(
        int count = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return Array.Empty<RoomLeaderboardEntry>();
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
        var entries = db.LeaderboardEntries.AsNoTracking()
            .Where(entry => entry.GameId != null && entry.PlayerId != null);
        var rows = await entries
            .GroupBy(entry => entry.PlayerId!.Value)
            .Select(group => new
            {
                PlayerId = group.Key,
                Wins = group.Count(entry => entry.Won),
                GamesPlayed = group.Count(),
                LastPlayedAtUtc = group.Max(entry => entry.PlayedAtUtc),
                Nickname = group.OrderByDescending(entry => entry.PlayedAtUtc)
                    .ThenByDescending(entry => entry.Id).Select(entry => entry.PlayerName).First()
            })
            .OrderByDescending(entry => entry.Wins)
            .ThenByDescending(entry => entry.GamesPlayed)
            .ThenBy(entry => entry.PlayerId)
            .Take(count)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new RoomLeaderboardEntry(
            row.PlayerId, row.Nickname, row.Wins, row.GamesPlayed,
            new DateTimeOffset(DateTime.SpecifyKind(row.LastPlayedAtUtc, DateTimeKind.Utc)))).ToArray();
    }
}
