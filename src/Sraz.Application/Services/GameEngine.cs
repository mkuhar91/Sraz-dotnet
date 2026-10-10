using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Application.Services;

public sealed class GameEngine : IGameEngine
{
    private readonly IQuestionProvider? _questionProvider;

    public GameEngine(IQuestionProvider? questionProvider = null)
    {
        _questionProvider = questionProvider;
    }

    public GameSession? CurrentSession { get; private set; }

    public void StartNewSession(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            throw new ArgumentException("Player name is required.", nameof(playerName));
        }

        CurrentSession = new GameSession
        {
            PlayerName = playerName,
            TotalQuestions = 0,
            CorrectAnswers = 0,
            WrongAnswers = 0,
            FinalScore = 0
        };
    }

    public async Task<GameSession> StartNewSessionAsync(
        string playerName,
        int questionCount = 10,
        string? category = null,
        string? difficulty = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            throw new ArgumentException("Player name is required.", nameof(playerName));
        }

        if (_questionProvider is null)
        {
            throw new InvalidOperationException("A question provider is required to start a round.");
        }

        var roundService = new QuizRoundService(_questionProvider);
        var round = await roundService.CreateRoundAsync(questionCount, category, difficulty, cancellationToken);

        var session = new GameSession
        {
            PlayerName = playerName,
            TotalQuestions = round.Questions.Count,
            CorrectAnswers = round.CorrectAnswers,
            WrongAnswers = round.WrongAnswers,
            FinalScore = round.Score,
            EndedAt = DateTimeOffset.UtcNow
        };

        CurrentSession = session;
        return session;
    }
}
