using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Application.Services;

public sealed class RandomNpcStrategy : INpcStrategy
{
    private readonly Random _random;
    private readonly double _accuracy;

    public RandomNpcStrategy(double accuracy = 0.6, int? seed = null)
    {
        if (accuracy is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(accuracy));
        }

        _accuracy = accuracy;
        _random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
    }

    public (Pawn Pawn, Direction Direction)? ChooseMove(IGameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var options = engine.CurrentPlayer.ActivePawns
            .SelectMany(pawn => engine.GetAvailableMoves(pawn)
                .Select(cell => (Pawn: pawn, Direction: DirectionTo(pawn, cell))))
            .ToList();
        return options.Count == 0 ? null : options[_random.Next(options.Count)];
    }

    public bool AnswerQuestionCorrectly() => _random.NextDouble() < _accuracy;

    private static Direction DirectionTo(Pawn pawn, BoardCell cell)
    {
        if (cell.Row < pawn.Row) return Direction.Up;
        if (cell.Row > pawn.Row) return Direction.Down;
        return cell.Column < pawn.Column ? Direction.Left : Direction.Right;
    }
}
