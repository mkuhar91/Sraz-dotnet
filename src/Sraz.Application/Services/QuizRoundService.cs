using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Application.Services;

public sealed class QuizRoundService(IQuestionProvider questionProvider) : IQuizRoundService
{
    public async Task<QuizRound> CreateRoundAsync(int numberOfQuestions = 10, CancellationToken cancellationToken = default)
    {
        var allQuestions = await questionProvider.GetQuestionsAsync(cancellationToken);

        var selected = allQuestions
            .OrderBy(_ => Guid.NewGuid())
            .Take(Math.Max(1, numberOfQuestions))
            .ToList();

        return new QuizRound
        {
            Questions = selected
        };
    }
}
