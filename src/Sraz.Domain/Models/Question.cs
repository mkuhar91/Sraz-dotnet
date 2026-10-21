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
    public string Explanation { get; init; } = string.Empty;

    public string CorrectAnswerText
    {
        get => CorrectOptionIndex >= 0 && CorrectOptionIndex < Options.Count
            ? Options[CorrectOptionIndex]
            : string.Empty;
    }
}
