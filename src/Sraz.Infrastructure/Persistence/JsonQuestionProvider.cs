using System.Text.Json;
using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Infrastructure.Persistence;

public sealed class JsonQuestionProvider(string filePath) : IQuestionProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<Question>> GetQuestionsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return Array.Empty<Question>();
        }

        List<QuestionRecord> records;
        try
        {
            await using var stream = File.OpenRead(filePath);
            records = await JsonSerializer.DeserializeAsync<List<QuestionRecord>>(stream, Options, cancellationToken)
                      ?? new List<QuestionRecord>();
        }
        catch (JsonException)
        {
            return Array.Empty<Question>();
        }

        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";

        return records
            .Select(r => Map(r, baseDirectory))
            .Where(q => q is not null)
            .Select(q => q!)
            .ToList();
    }

    private static Question? Map(QuestionRecord? record, string baseDirectory)
    {
        if (record is null || string.IsNullOrWhiteSpace(record.Text))
        {
            return null;
        }

        var type = ParseType(record.Type);
        var options = record.Options ?? new List<string>();
        var accepted = (record.AcceptedAnswers ?? new List<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToList();

        switch (type)
        {
            case QuestionType.TextInput:
                if (accepted.Count == 0)
                {
                    return null;
                }

                options = new List<string>();
                break;
            case QuestionType.TrueFalse:
                if (options.Count != 2 || !IsValidIndex(record.CorrectOptionIndex, options.Count))
                {
                    return null;
                }

                break;
            default:
                if (options.Count < 2 || !IsValidIndex(record.CorrectOptionIndex, options.Count))
                {
                    return null;
                }

                break;
        }

        return new Question
        {
            Type = type,
            Category = record.Category ?? string.Empty,
            Difficulty = record.Difficulty,
            Text = record.Text,
            ImagePath = ResolveImage(record.ImagePath, baseDirectory),
            Options = options,
            CorrectOptionIndex = type == QuestionType.TextInput ? -1 : record.CorrectOptionIndex,
            AcceptedAnswers = accepted,
            Explanation = record.Explanation ?? string.Empty
        };
    }

    private static QuestionType ParseType(string? type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "truefalse" or "true_false" or "boolean" => QuestionType.TrueFalse,
            "textinput" or "text" => QuestionType.TextInput,
            _ => QuestionType.MultipleChoice
        };
    }

    private static bool IsValidIndex(int index, int count) => index >= 0 && index < count;

    private static string? ResolveImage(string? imagePath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var full = Path.IsPathRooted(imagePath)
            ? imagePath
            : Path.GetFullPath(Path.Combine(baseDirectory, imagePath));

        return File.Exists(full) ? full : null;
    }
}
