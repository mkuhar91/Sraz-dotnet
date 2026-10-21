namespace Sraz.Infrastructure.Persistence;

public sealed class LeaderboardEntryEntity
{
    public long Id { get; set; }
    public Guid? GameId { get; set; }
    public Guid? PlayerId { get; set; }
    public bool Won { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int Score { get; set; }
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public int Timeouts { get; set; }
    public int BestStreak { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public DateTime PlayedAtUtc { get; set; }
}
