namespace Sraz.Infrastructure.Persistence;

public sealed class QuestionEntity
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public int Difficulty { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public string OptionsJson { get; set; } = "[]";
    public int CorrectOptionIndex { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public int Type { get; set; }
}
