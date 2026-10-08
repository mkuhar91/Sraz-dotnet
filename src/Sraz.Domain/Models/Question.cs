namespace Sraz.Domain.Models;

public sealed class Question
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Category { get; init; } = string.Empty;
    public int Difficulty { get; init; }
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();
    public int CorrectOptionIndex { get; init; }
    public string Explanation { get; init; } = string.Empty;
}
