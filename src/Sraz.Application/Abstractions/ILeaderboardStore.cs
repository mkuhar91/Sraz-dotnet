using Sraz.Domain.Models;

namespace Sraz.Application.Abstractions;

public interface ILeaderboardStore
{
    List<LeaderboardEntry> Load();

    void Save(IEnumerable<LeaderboardEntry> entries);
}
