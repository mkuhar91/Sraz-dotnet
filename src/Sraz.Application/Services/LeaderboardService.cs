using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Application.Services;

public sealed record LeaderboardFilter(
    string? Category = null,
    string? Difficulty = null,
    string? Mode = null,
    DateTime? SinceUtc = null)
{
    public bool Matches(LeaderboardEntry entry)
    {
        return Matches(Category, entry.Category)
               && Matches(Difficulty, entry.Difficulty)
               && Matches(Mode, entry.Mode)
               && (SinceUtc is null || entry.PlayedAtUtc >= SinceUtc.Value);
    }

    private static bool Matches(string? wanted, string actual)
    {
        return string.IsNullOrWhiteSpace(wanted)
               || string.Equals(wanted, "All", StringComparison.OrdinalIgnoreCase)
               || string.Equals(wanted, actual, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class LeaderboardService
{
    private const int MaxStoredEntries = 500;
    private static readonly object Sync = new();

    private readonly ILeaderboardStore _store;

    public LeaderboardService(ILeaderboardStore store)
    {
        _store = store;
    }

    public List<LeaderboardEntry> GetTopEntries(int count = 10, LeaderboardFilter? filter = null)
    {
        IEnumerable<LeaderboardEntry> entries = _store.Load();
        if (filter is not null)
        {
            entries = entries.Where(filter.Matches);
        }

        return entries
            .OrderByDescending(x => x.Score)
            .Take(count)
            .ToList();
    }

    public void SaveEntry(LeaderboardEntry entry)
    {
        lock (Sync)
        {
            var entries = _store.Load();
            entries.Add(entry);

            var trimmed = entries
                .OrderByDescending(x => x.Score)
                .Take(MaxStoredEntries)
                .ToList();

            _store.Save(trimmed);
        }
    }
}
