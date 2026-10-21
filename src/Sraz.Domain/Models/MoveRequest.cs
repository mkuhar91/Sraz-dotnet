namespace Sraz.Domain.Models;

public sealed class MoveRequest
{
    public required BoardCell Target { get; init; }
    public required IReadOnlyList<Question> Questions { get; init; }
    public bool IsDuel { get; init; }
    public bool IsBlocked { get; init; }
}
