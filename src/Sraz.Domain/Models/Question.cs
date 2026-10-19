using System.Globalization;
using System.Text;

namespace Sraz.Domain.Models;

public sealed class Question
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public QuestionType Type { get; init; } = QuestionType.MultipleChoice;
    public string Category { get; init; } = string.Empty;
    public int Difficulty { get; init; }
    public string Text { get; init; } = string.Empty;
    public string? ImagePath { get; init; }
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();
    public int CorrectOptionIndex { get; init; }
    public IReadOnlyList<string> AcceptedAnswers { get; init; } = Array.Empty<string>();
    public string Explanation { get; init; } = string.Empty;

    public string CorrectAnswerText
    {
        get
        {
            if (Type == QuestionType.TextInput)
            {
                return AcceptedAnswers.Count > 0 ? AcceptedAnswers[0] : string.Empty;
            }

            return CorrectOptionIndex >= 0 && CorrectOptionIndex < Options.Count
                ? Options[CorrectOptionIndex]
                : string.Empty;
        }
    }

    public bool IsTextAnswerCorrect(string? answer)
    {
        var normalized = Normalize(answer);
        if (normalized.Length == 0)
        {
            return false;
        }

        return AcceptedAnswers.Any(a => Normalize(a) == normalized);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            previousWasSpace = false;
            builder.Append(c);
        }

        return builder.ToString();
    }
}
