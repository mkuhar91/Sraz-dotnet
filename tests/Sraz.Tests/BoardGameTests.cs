using Sraz.Application.Abstractions;
using Sraz.Application.Services;
using Sraz.Domain.Models;
using Xunit;

namespace Sraz.Tests;

public class BoardGameTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    public void StartGame_CreatesThreePawnsForEveryPlayer(int humanPlayers, int expectedPlayers)
    {
        var engine = CreateEngine();
        var names = Enumerable.Range(0, humanPlayers).Select(index => $"Player {index}").ToArray();

        engine.StartGame(names, 6, 42);

        Assert.Equal(expectedPlayers, engine.Players.Count);
        Assert.All(engine.Players, player => Assert.Equal(3, player.ActivePawns.Count));
        Assert.Equal(expectedPlayers * 3, engine.Board.Cells.Cast<BoardCell>().Count(cell => cell.Occupant is not null));
        if (humanPlayers == 1)
        {
            Assert.True(engine.Players[1].IsNpc);
            Assert.Equal((5, 5), (engine.Players[1].Pawns[0].Row, engine.Players[1].Pawns[0].Column));
        }
    }

    [Fact]
    public void StartGame_RejectsMoreThanFourPlayers()
    {
        var engine = CreateEngine();

        Assert.Throws<ArgumentException>(() => engine.StartGame(["A", "B", "C", "D", "E"]));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(15)]
    public void StartGame_UsesDistinctCornerStartingPositions(int boardSize)
    {
        var engine = CreateEngine();
        engine.StartGame(["A", "B", "C", "D"], boardSize, 7);

        Assert.Equal(new[] { (0, 0), (0, boardSize - 1), (boardSize - 1, 0), (boardSize - 1, boardSize - 1) },
            engine.Players.Select(player => (player.Pawns[0].Row, player.Pawns[0].Column)));
        Assert.Equal(12, engine.Board.Cells.Cast<BoardCell>().Count(cell => cell.Occupant is not null));
    }

    [Theory]
    [InlineData(2, new[] { 0, 3 })]
    [InlineData(3, new[] { 0, 1, 2 })]
    public void StartGame_AssignsExpectedOppositeOrFirstCorners(int playerCount, int[] corners)
    {
        var engine = CreateEngine();
        engine.StartGame(Enumerable.Range(0, playerCount).Select(index => $"P{index}").ToArray(), 6, 8);
        var expectedCorners = corners.Select(corner => corner switch
        {
            0 => (0, 0),
            1 => (0, 5),
            2 => (5, 0),
            _ => (5, 5)
        });

        Assert.Equal(expectedCorners, engine.Players.Select(player => (player.Pawns[0].Row, player.Pawns[0].Column)));
    }

    [Fact]
    public async Task OwnPawnBlocksMoveWithoutConsumingAction()
    {
        var engine = CreateEngine();
        engine.StartGame(["Alice", "Bob"], 6, 1);
        var pawn = engine.Players[0].Pawns[0];

        var request = await engine.RequestMove(pawn, Direction.Right);
        var result = engine.ResolveMove(pawn, Direction.Right, true);

        Assert.True(request.IsBlocked);
        Assert.Equal(MoveOutcome.Blocked, result.Outcome);
        Assert.False(pawn.HasActedThisTurn);
    }

    [Fact]
    public async Task IncorrectAnswerConsumesActionAndThreeActionsAdvanceTurn()
    {
        var engine = CreateEngine();
        engine.StartGame(["Alice", "Bob"], 6, 1);
        var pawns = engine.Players[0].Pawns;

        var questionRequest = await engine.RequestMove(pawns[1], Direction.Down);
        var result = engine.ResolveMove(pawns[1], Direction.Down, false);
        engine.ResolveMove(pawns[2], Direction.Down, true);
        var last = engine.ResolveMove(pawns[0], Direction.Down, true);

        Assert.Equal(engine.Board.GetCell(1, 1).Theme, ThemeForCategory(questionRequest.Questions[0].Category));
        Assert.Equal(MoveOutcome.Stayed, result.Outcome);
        Assert.True(pawns[1].HasActedThisTurn);
        Assert.True(last.TurnCompleted);
        Assert.Equal("Bob", engine.CurrentPlayer.Name);

        var bobPawns = engine.CurrentPlayer.Pawns;
        engine.ResolveMove(bobPawns[1], Direction.Up, true);
        engine.ResolveMove(bobPawns[2], Direction.Up, true);
        engine.ResolveMove(bobPawns[0], Direction.Left, true);

        Assert.Equal("Alice", engine.CurrentPlayer.Name);
        Assert.All(engine.CurrentPlayer.ActivePawns, pawn => Assert.False(pawn.HasActedThisTurn));
    }

    [Fact]
    public async Task DuelRequiresThreeCorrectAnswersAndCapturesLastPawnToWin()
    {
        var engine = CreateEngine();
        engine.StartGame(["Alice", "Bob"], 6, 4);
        var attacker = engine.Players[0].Pawns[1];
        var defender = engine.Players[1].Pawns[0];
        engine.Board.MovePawn(defender, engine.Board.GetCell(0, 2));
        foreach (var otherPawn in engine.Players[1].Pawns.Skip(1))
        {
            engine.Board.CapturePawn(otherPawn);
        }

        var request = await engine.RequestMove(attacker, Direction.Right);
        var result = engine.ResolveDuel(attacker, Direction.Right, [true, true, true]);

        Assert.True(request.IsDuel);
        Assert.Equal(3, request.Questions.Count);
        Assert.Equal(3, request.Questions.Count(question => question.Type == QuestionType.MultipleChoice));
        Assert.Equal(MoveOutcome.Captured, result.Outcome);
        Assert.True(defender.IsCaptured);
        Assert.Equal((0, 2), (attacker.Row, attacker.Column));
        Assert.Equal(GameState.Finished, engine.State);
        Assert.Equal("Alice", engine.Winner?.Name);
        Assert.Throws<InvalidOperationException>(() => engine.ResolveMove(attacker, Direction.Up, true));
    }

    [Fact]
    public async Task FailedDuelStopsOnFirstIncorrectAndConsumesAction()
    {
        var engine = CreateEngine();
        engine.StartGame(["Alice", "Bob"], 6, 4);
        var attacker = engine.Players[0].Pawns[1];
        var defender = engine.Players[1].Pawns[0];
        engine.Board.MovePawn(defender, engine.Board.GetCell(0, 2));

        var request = await engine.RequestMove(attacker, Direction.Right);
        var result = engine.ResolveDuel(attacker, Direction.Right, [false]);

        Assert.True(request.IsDuel);
        Assert.Equal(MoveOutcome.Stayed, result.Outcome);
        Assert.True(attacker.HasActedThisTurn);
        Assert.False(defender.IsCaptured);
    }

    [Fact]
    public async Task EliminatedPlayerIsSkippedInThreePlayerGame()
    {
        var engine = CreateEngine();
        engine.StartGame(["Alice", "Bob", "Cara"], 6, 3);
        var attacker = engine.Players[0].Pawns[1];
        var defender = engine.Players[1].Pawns[0];
        engine.Board.MovePawn(defender, engine.Board.GetCell(0, 2));
        foreach (var otherPawn in engine.Players[1].Pawns.Skip(1))
        {
            engine.Board.CapturePawn(otherPawn);
        }

        var request = await engine.RequestMove(attacker, Direction.Right);
        Assert.True(request.IsDuel);
        engine.ResolveDuel(attacker, Direction.Right, [true, true, true]);

        Assert.True(engine.Players[1].IsEliminated);
        Assert.Equal(GameState.InProgress, engine.State);
        Assert.Null(engine.Winner);

        engine.NextTurn();

        Assert.Equal("Cara", engine.CurrentPlayer.Name);
    }

    [Fact]
    public void RandomNpcStrategyOnlySelectsAvailableMoves()
    {
        var engine = CreateEngine();
        engine.StartGame(["Alice"], 6, 5);
        var strategy = new RandomNpcStrategy(accuracy: 0.6, seed: 42);
        engine.ResolveMove(engine.Players[0].Pawns[1], Direction.Down, true);
        engine.ResolveMove(engine.Players[0].Pawns[2], Direction.Down, true);
        engine.ResolveMove(engine.Players[0].Pawns[0], Direction.Right, true);

        var choice = strategy.ChooseMove(engine);

        Assert.NotNull(choice);
        Assert.Contains(engine.GetAvailableMoves(choice.Value.Pawn),
            cell => cell.Row == choice.Value.Pawn.Row + (choice.Value.Direction == Direction.Down ? 1 : choice.Value.Direction == Direction.Up ? -1 : 0) &&
                    cell.Column == choice.Value.Pawn.Column + (choice.Value.Direction == Direction.Right ? 1 : choice.Value.Direction == Direction.Left ? -1 : 0));
    }

    private static GameEngine CreateEngine()
    {
        return new GameEngine(new FakeQuestionProvider());
    }

    private static QuestionTheme ThemeForCategory(string category) => category switch
    {
        "Geography" => QuestionTheme.Geography,
        "History" => QuestionTheme.History,
        "Math" => QuestionTheme.Math,
        "Science" => QuestionTheme.Science,
        "Sports" => QuestionTheme.Sports,
        "Art" => QuestionTheme.Art,
        "Literature" => QuestionTheme.Literature,
        _ => QuestionTheme.Entertainment
    };

    private sealed class FakeQuestionProvider : IQuestionProvider
    {
        public Task<IReadOnlyList<Question>> GetQuestionsAsync(CancellationToken cancellationToken = default)
        {
            var questions = Enum.GetValues<QuestionTheme>().SelectMany(theme =>
            {
                var category = theme.ToString();
                var choices = Enumerable.Range(0, 3).Select(index => new Question
                {
                    Category = category,
                    Text = $"Choice question {index}",
                    Options = ["A", "B"],
                    CorrectOptionIndex = 0
                });
                return choices;
            }).ToList();
            return Task.FromResult<IReadOnlyList<Question>>(questions);
        }
    }
}
