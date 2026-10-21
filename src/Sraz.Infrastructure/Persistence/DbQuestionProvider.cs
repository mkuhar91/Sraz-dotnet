using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Infrastructure.Persistence;

public sealed class DbQuestionProvider(SrazDbContext dbContext) : IQuestionProvider
{
    public Task<IReadOnlyList<Question>> GetQuestionsAsync(CancellationToken cancellationToken = default) =>
        GetQuestionsAsync(category: null, cancellationToken);

    public async Task<IReadOnlyList<Question>> GetQuestionsAsync(
        string? category,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Questions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(question => question.Category == category);
        }

        var entities = await query.ToListAsync(cancellationToken);
        return entities
            .Select(Map)
            .Where(question => question is not null)
            .Select(question => question!)
            .ToList();
    }

    private static Question? Map(QuestionEntity entity)
    {
        try
        {
            var options = JsonSerializer.Deserialize<List<string>>(entity.OptionsJson);
            if (string.IsNullOrWhiteSpace(entity.Text) || options is not { Count: >= 2 }
                || entity.CorrectOptionIndex < 0 || entity.CorrectOptionIndex >= options.Count)
            {
                return null;
            }

            return new Question
            {
                Id = entity.Id,
                Type = (QuestionType)entity.Type,
                Category = entity.Category,
                Difficulty = entity.Difficulty,
                Text = entity.Text,
                ImagePath = entity.ImagePath,
                Options = options,
                CorrectOptionIndex = entity.CorrectOptionIndex,
                Explanation = entity.Explanation
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
