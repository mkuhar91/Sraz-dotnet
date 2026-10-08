using Sraz.Domain.Models;

namespace Sraz.Application.Abstractions;

public interface IQuizRoundService
{
    Task<QuizRound> CreateRoundAsync(int numberOfQuestions = 10, CancellationToken cancellationToken = default);
}
