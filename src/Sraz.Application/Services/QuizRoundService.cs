using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Application.Services;

public sealed class QuizRoundService : IQuizRoundService
{
    private const int DailyQuestionCount = 10;

    private readonly IQuestionProvider _provider;
    private readonly TimeProvider _timeProvider;

    public QuizRoundService(IQuestionProvider provider, TimeProvider? timeProvider = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<QuizRound> CreateRoundAsync(
        int numberOfQuestions = 10,
        string? category = null,
        string? difficulty = null,
        GameMode mode = GameMode.Classic,
        CancellationToken cancellationToken = default)
    {
        var allQuestions = await _provider.GetQuestionsAsync(cancellationToken);

        if (mode == GameMode.Daily)
        {
            return CreateDailyRound(allQuestions);
        }

        var filtered = Filter(allQuestions, category, difficulty);
        var shuffled = filtered.OrderBy(_ => Guid.NewGuid()).ToList();

        return mode switch
        {
            GameMode.Survival => new QuizRound
            {
                Mode = mode,
                Questions = shuffled,
                MaxMistakes = QuizRound.SurvivalMistakes
            },
            GameMode.TimeAttack => new QuizRound
            {
                Mode = mode,
                Questions = shuffled,
                TimeLimitSeconds = QuizRound.TimeAttackSeconds
            },
            _ => new QuizRound
            {
                Mode = mode,
                Questions = shuffled.Take(Math.Min(Math.Max(numberOfQuestions, 1), shuffled.Count)).ToList()
            }
        };
    }

    private QuizRound CreateDailyRound(IReadOnlyList<Question> allQuestions)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var random = new Random(today.Year * 10000 + today.Month * 100 + today.Day);

        var ordered = allQuestions
            .OrderBy(q => q.Text, StringComparer.Ordinal)
            .ThenBy(q => q.Category, StringComparer.Ordinal)
            .ToList();

        for (var i = ordered.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
        }

        return new QuizRound
        {
            Mode = GameMode.Daily,
            Questions = ordered.Take(DailyQuestionCount).ToList()
        };
    }

    private static IReadOnlyList<Question> Filter(IReadOnlyList<Question> questions, string? category, string? difficulty)
    {
        IEnumerable<Question> filtered = questions;

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(q => q.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(difficulty))
        {
            var normalizedDifficulty = NormalizeDifficulty(difficulty);
            if (normalizedDifficulty is not null)
            {
                filtered = filtered.Where(q => q.Difficulty == normalizedDifficulty.Value);
            }
        }

        return filtered.ToList();
    }

    private static int? NormalizeDifficulty(string difficulty)
    {
        return difficulty.Trim().ToLowerInvariant() switch
        {
            "easy" => 1,
            "medium" => 2,
            "hard" => 3,
            _ => null
        };
    }
}
