using Sraz.Domain.Models;

namespace Sraz.Application.Rooms;

/// <summary>Question as seen by a client. It deliberately has no correct answer or explanation.</summary>
public sealed record QuestionDto(
    Guid QuestionId,
    string Category,
    string Text,
    string? ImagePath,
    IReadOnlyList<string> Options,
    int Number,
    int Total,
    bool IsDuel,
    DateTimeOffset ExpiresAtUtc);

public sealed record AnswerResultDto(
    bool WasCorrect,
    bool IsCompleted,
    MoveOutcome? Outcome,
    QuestionDto? NextQuestion);
