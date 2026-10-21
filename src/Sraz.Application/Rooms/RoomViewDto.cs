using Sraz.Domain.Models;

namespace Sraz.Application.Rooms;

public sealed record PlayerViewDto(
    int Index,
    Guid Id,
    string Name,
    string Color,
    bool IsNpc,
    bool IsHost,
    int PawnsRemaining,
    bool IsDisconnected = false,
    Guid? OriginalPlayerId = null);

public sealed record PawnViewDto(
    Guid Id,
    int OwnerIndex,
    int Row,
    int Column,
    bool IsCaptured,
    bool HasActedThisTurn);

/// <summary>A cell a pawn of the viewing player may move to right now.</summary>
public sealed record MoveTargetDto(Guid PawnId, Direction Direction, int Row, int Column);

/// <summary>
/// Read-only view of a room for rendering. Contains board themes, pawns and the viewer's allowed moves,
/// but never a correct answer. <see cref="Themes"/> is row-major with <c>BoardSize * BoardSize</c> entries.
/// </summary>
public sealed record BoardViewDto(
    string Code,
    RoomState State,
    int BoardSize,
    IReadOnlyList<QuestionTheme> Themes,
    IReadOnlyList<PlayerViewDto> Players,
    IReadOnlyList<PawnViewDto> Pawns,
    int CurrentPlayerIndex,
    int? WinnerIndex,
    int ViewerIndex,
    bool HasPendingQuestion,
    IReadOnlyList<MoveTargetDto> AllowedMoves)
{
    public bool IsViewerHost => ViewerIndex >= 0 && Players[ViewerIndex].IsHost;
    public bool IsViewerTurn => ViewerIndex >= 0 && ViewerIndex == CurrentPlayerIndex;
    public PlayerViewDto? Host => Players.FirstOrDefault(player => player.IsHost);
}
