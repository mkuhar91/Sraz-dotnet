using Sraz.Domain.Models;

namespace Sraz.Application.Abstractions;

public interface IGameEngine
{
    void StartNewSession(string playerName);
    void StartGame(IReadOnlyList<string> playerNames, int boardSize = Board.DefaultSize, int? seed = null);
    Board Board { get; }
    IReadOnlyList<Player> Players { get; }
    Player CurrentPlayer { get; }
    Player? Winner { get; }
    GameState State { get; }
    IReadOnlyList<BoardCell> GetAvailableMoves(Pawn pawn);
    Task<MoveRequest> RequestMove(Pawn pawn, Direction direction, CancellationToken cancellationToken = default);
    MoveResolution ResolveMove(Pawn pawn, Direction direction, bool isCorrect);
    MoveResolution ResolveDuel(Pawn pawn, Direction direction, IReadOnlyList<bool> answersCorrect);
}
