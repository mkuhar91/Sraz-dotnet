using Sraz.Application.Services;
using Sraz.Domain.Models;
using Sraz.Infrastructure.Persistence;
using Xunit;

namespace Sraz.Tests;

public class LeaderboardTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sraz-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "leaderboard.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static LeaderboardEntry Entry(string name, int score) => new() { PlayerName = name, Score = score };

    [Fact]
    public void Load_WhenFileMissing_ReturnsEmpty()
    {
        var store = new LeaderboardStore(FilePath);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_WhenFileCorrupt_ReturnsEmptyAndKeepsBackup()
    {
        var store = new LeaderboardStore(FilePath);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Empty(store.Load());
        Assert.True(File.Exists(FilePath + ".corrupt"));
    }

    [Fact]
    public void SaveEntry_PersistsAndReturnsTopEntriesOrdered()
    {
        var service = new LeaderboardService(new LeaderboardStore(FilePath));

        service.SaveEntry(Entry("A", 100));
        service.SaveEntry(Entry("B", 300));
        service.SaveEntry(Entry("C", 200));

        var top = service.GetTopEntries(2);

        Assert.Equal(new[] { "B", "C" }, top.Select(x => x.PlayerName));
    }

    [Fact]
    public void SaveEntry_LimitsStoredEntries()
    {
        var store = new LeaderboardStore(FilePath);
        var service = new LeaderboardService(store);

        for (var i = 0; i < 520; i++)
        {
            service.SaveEntry(Entry("P" + i, i));
        }

        Assert.Equal(500, store.Load().Count);
    }

    [Fact]
    public async Task JsonQuestionProvider_SkipsInvalidRecords()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "q.json");
        await File.WriteAllTextAsync(path,
            "[{\"category\":\"A\",\"difficulty\":1,\"text\":\"ok\",\"options\":[\"x\",\"y\"],\"correctOptionIndex\":1}," +
            "{\"category\":\"A\",\"difficulty\":1,\"text\":\"bad index\",\"options\":[\"x\",\"y\"],\"correctOptionIndex\":5}," +
            "{\"category\":\"A\",\"difficulty\":1,\"text\":\"no options\"}]");

        var questions = await new JsonQuestionProvider(path).GetQuestionsAsync();

        Assert.Single(questions);
    }

    [Fact]
    public async Task JsonQuestionProvider_WhenMalformed_ReturnsEmpty()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "q.json");
        await File.WriteAllTextAsync(path, "oops");

        Assert.Empty(await new JsonQuestionProvider(path).GetQuestionsAsync());
    }

    [Fact]
    public void QuizRound_ScoreNeverGoesNegative()
    {
        var round = new QuizRound
        {
            Questions = new[]
            {
                new Question { Text = "q", Options = new[] { "a", "b" }, CorrectOptionIndex = 0 }
            }
        };

        round.RegisterTimeout();

        Assert.Equal(0, round.Score);
    }
}
