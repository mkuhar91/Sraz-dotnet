namespace Sraz.Domain.Models;

public sealed class GameSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string PlayerName { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public int TotalQuestions { get; set; }
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public int FinalScore { get; set; }
}
