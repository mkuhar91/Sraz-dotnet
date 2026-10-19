using Sraz.Domain.Models;

namespace Sraz.Application.Abstractions;

public interface IQuizRoundService
{
    Task<QuizRound> CreateRoundAsync(
        int numberOfQuestions = 10,
        string? category = null,
        string? difficulty = null,
        GameMode mode = GameMode.Classic,
        CancellationToken cancellationToken = default);
}
