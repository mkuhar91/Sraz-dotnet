using Sraz.Domain.Models;

namespace Sraz.Application.Services;

public sealed class LeaderboardService
{
    private readonly LeaderboardStore _store;

    public LeaderboardService(LeaderboardStore store)
    {
        _store = store;
    }

    public List<LeaderboardEntry> GetTopEntries(int count = 10)
    {
        return _store.Load()
            .OrderByDescending(x => x.Score)
            .Take(count)
            .ToList();
    }

    public void SaveEntry(LeaderboardEntry entry)
    {
        var entries = _store.Load();
        entries.Add(entry);
        _store.Save(entries);
    }
}
