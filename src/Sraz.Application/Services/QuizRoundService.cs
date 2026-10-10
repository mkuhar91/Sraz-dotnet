using System.IO;
using Sraz.Application.Services;
using Sraz.Domain.Models;
using Sraz.Infrastructure.Persistence;

namespace Sraz.Application.Services;

public sealed class QuizRoundService
{
    private readonly QuestionProvider _provider;

    public QuizRoundService(QuestionProvider provider)
    {
        _provider = provider;
    }

    public async Task<QuizRound> CreateRoundAsync(int count, string? category = null, string? difficulty = null)
    {
        var allQuestions = await _provider.GetQuestionsAsync();
        var filtered = allQuestions;

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(q => q.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(difficulty))
        {
            filtered = filtered.Where(q => q.Difficulty.Equals(difficulty, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (filtered.Count == 0)
        {
            return new QuizRound { Questions = new List<Question>() };
        }

        var questions = filtered
            .OrderBy(_ => Guid.NewGuid())
            .Take(Math.Min(count, filtered.Count))
            .ToList();

        return new QuizRound { Questions = questions };
    }
}
