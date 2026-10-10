using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Application.Services;

public sealed class QuizRoundService : IQuizRoundService
{
    private readonly IQuestionProvider _provider;

    public QuizRoundService(IQuestionProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<QuizRound> CreateRoundAsync(
        int numberOfQuestions = 10,
        string? category = null,
        string? difficulty = null,
        CancellationToken cancellationToken = default)
    {
        var allQuestions = await _provider.GetQuestionsAsync(cancellationToken);
        var filtered = allQuestions;

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered
                .Where(q => q.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(difficulty))
        {
            var normalizedDifficulty = NormalizeDifficulty(difficulty);
            if (normalizedDifficulty is not null)
            {
                filtered = filtered
                    .Where(q => q.Difficulty == normalizedDifficulty.Value)
                    .ToList();
            }
        }

        if (filtered.Count == 0)
        {
            return new QuizRound { Questions = Array.Empty<Question>() };
        }

        var questions = filtered
            .OrderBy(_ => Guid.NewGuid())
            .Take(Math.Min(Math.Max(numberOfQuestions, 1), filtered.Count))
            .ToList();

        return new QuizRound { Questions = questions };
    }

    private static int? NormalizeDifficulty(string difficulty)
    {
        return difficulty.Trim() switch
        {
            "Easy" => 1,
            "Medium" => 2,
            "Hard" => 3,
            "easy" => 1,
            "medium" => 2,
            "hard" => 3,
            _ => null
        };
    }
}
