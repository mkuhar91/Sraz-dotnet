namespace Sraz.Domain.Models;

public sealed class Player
{
    public string Name { get; init; } = string.Empty;
    public int Index { get; init; }
    public string Color { get; init; } = string.Empty;
    public bool IsNpc { get; init; }
    public List<Pawn> Pawns { get; init; } = new();
    public bool IsEliminated => Pawns.All(pawn => pawn.IsCaptured);
    public IReadOnlyList<Pawn> ActivePawns => Pawns.Where(pawn => !pawn.IsCaptured).ToList();
}
