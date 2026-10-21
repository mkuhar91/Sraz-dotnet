using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Infrastructure.Persistence;

public sealed class QuestionSeedImporter(SrazDbContext dbContext, IQuestionProvider seedProvider)
{
    public async Task<int> ImportAsync(CancellationToken cancellationToken = default)
    {
        var seedQuestions = await seedProvider.GetQuestionsAsync(cancellationToken);
        var existingQuestions = await dbContext.Questions
            .Select(question => new { question.Category, question.Text })
            .ToListAsync(cancellationToken);
        var existingKeys = existingQuestions
            .Select(question => Key(question.Category, question.Text))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var question in seedQuestions)
        {
            if (!existingKeys.Add(Key(question.Category, question.Text)))
            {
                continue;
            }

            dbContext.Questions.Add(Map(question));
            added++;
        }

        if (added > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return added;
    }

    private static string Key(string category, string text) => $"{category}\0{text}";

    private static QuestionEntity Map(Question question) => new()
    {
        Id = question.Id,
        Category = question.Category,
        Difficulty = question.Difficulty,
        Text = question.Text,
        ImagePath = question.ImagePath,
        OptionsJson = JsonSerializer.Serialize(question.Options),
        CorrectOptionIndex = question.CorrectOptionIndex,
        Explanation = question.Explanation,
        Type = (int)question.Type
    };
}
