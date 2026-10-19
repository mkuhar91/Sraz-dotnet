using System.Text.Json.Serialization;

namespace Sraz.Infrastructure.Persistence;

internal sealed class QuestionRecord
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("imagePath")]
    public string? ImagePath { get; set; }

    [JsonPropertyName("options")]
    public List<string>? Options { get; set; } = new();

    [JsonPropertyName("correctOptionIndex")]
    public int CorrectOptionIndex { get; set; }

    [JsonPropertyName("acceptedAnswers")]
    public List<string>? AcceptedAnswers { get; set; }

    [JsonPropertyName("explanation")]
    public string? Explanation { get; set; } = string.Empty;
}
