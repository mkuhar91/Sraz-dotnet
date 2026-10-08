using System.Text.Json;
using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Infrastructure.Persistence;

public sealed class JsonQuestionProvider(string filePath) : IQuestionProvider
{
    public async Task<IReadOnlyList<Question>> GetQuestionsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return Array.Empty<Question>();
        }

        await using var stream = File.OpenRead(filePath);
        var records = await JsonSerializer.DeserializeAsync<List<QuestionRecord>>(stream, cancellationToken: cancellationToken)
                      ?? new List<QuestionRecord>();

        return records
            .Where(r => r.Options.Count >= 2 && !string.IsNullOrWhiteSpace(r.Text))
            .Select(r => new Question
            {
                Category = r.Category,
                Difficulty = r.Difficulty,
                Text = r.Text,
                Options = r.Options,
                CorrectOptionIndex = r.CorrectOptionIndex,
                Explanation = r.Explanation
            })
            .ToList();
    }
}
