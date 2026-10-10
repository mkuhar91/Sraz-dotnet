using System.Text.Json.Serialization;

namespace Sraz.Domain.Models;

public sealed class LeaderboardEntry
{
    public string PlayerName { get; set; } = string.Empty;
    public int Score { get; set; }
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public int Timeouts { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public DateTime PlayedAtUtc { get; set; } = DateTime.UtcNow;
}
