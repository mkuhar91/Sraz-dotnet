namespace Sraz.Application.Abstractions;

public interface IRoomLeaderboard
{
    Task<IReadOnlyList<RoomLeaderboardEntry>> GetTopAsync(
        int count = 100, CancellationToken cancellationToken = default);
}

public sealed record RoomLeaderboardEntry(
    Guid PlayerId, string Nickname, int Wins, int GamesPlayed, DateTimeOffset LastPlayedAtUtc);
