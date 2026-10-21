namespace Sraz.Domain.Models;

public sealed class MoveResolution
{
    public required MoveOutcome Outcome { get; init; }
    public bool TurnCompleted { get; init; }
    public Player? EliminatedPlayer { get; init; }
    public Player? Winner { get; init; }
}
