namespace Sraz.Domain.Models;

public sealed class Pawn
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int OwnerIndex { get; init; }
    public int Row { get; internal set; }
    public int Column { get; internal set; }
    public bool HasActedThisTurn { get; internal set; }
    public bool IsCaptured { get; internal set; }
}
