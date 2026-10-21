namespace Sraz.Application.Services;

public sealed record PawnState(Guid Id, int OwnerIndex, int Row, int Column, bool IsCaptured, bool HasActedThisTurn);
