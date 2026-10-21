using System.Reflection;
using System.Text.Json;
using Sraz.Application.Rooms;
using Sraz.Domain.Models;
using Xunit;

namespace Sraz.Tests;

public sealed class RoomGameplayTests
{
    private const int Right = TestQuestionProvider.CorrectIndex;
    private const int Wrong = 0;

    [Fact]
    public async Task MoveByPlayerWhoIsNotOnTurnIsRejected()
    {
        var context = new RoomTestContext();
        var (room, _, p1) = await context.StartTwoPlayerGameAsync();
        var (pawnId, direction) = RoomTestContext.FindMove(await room.GetSnapshotAsync(), 1);

        var result = await room.BeginMoveAsync(p1, pawnId, direction);

        Assert.Equal(RoomError.NotYourTurn, result.Error);
        Assert.Null((await room.GetSnapshotAsync()).Game!.Pending);
    }

    [Fact]
    public async Task MoveWithSomeoneElsesPawnOrBySpectatorIsRejected()
    {
        var context = new RoomTestContext();
        var (room, p0, _) = await context.StartTwoPlayerGameAsync();
        var snapshot = await room.GetSnapshotAsync();
        var foreignPawn = snapshot.Game!.Pawns.First(pawn => pawn.OwnerIndex == 1).Id;

        Assert.Equal(RoomError.PawnNotOwned, (await room.BeginMoveAsync(p0, foreignPawn, Direction.Up)).Error);
        Assert.Equal(RoomError.PawnNotOwned, (await room.BeginMoveAsync(p0, Guid.NewGuid(), Direction.Up)).Error);
        Assert.Equal(RoomError.PlayerNotInRoom, (await room.BeginMoveAsync(Guid.NewGuid(), foreignPawn, Direction.Up)).Error);
        Assert.Equal(RoomError.InvalidMove, (await room.BeginMoveAsync(p0, snapshot.Game.Pawns.First(pawn => pawn.OwnerIndex == 0).Id, (Direction)99)).Error);
    }

    [Fact]
    public async Task AnswerFromOtherPlayerOrWithoutQuestionIsRejected()
    {
        var context = new RoomTestContext();
        var (room, p0, p1) = await context.StartTwoPlayerGameAsync();

        Assert.Equal(RoomError.NoPendingQuestion, (await room.AnswerAsync(p0, Right)).Error);
        await RoomTestContext.BeginAnyMoveAsync(room, p0, 0);

        Assert.Equal(RoomError.NotYourTurn, (await room.AnswerAsync(p1, Right)).Error);
        Assert.Equal(RoomError.InvalidAnswer, (await room.AnswerAsync(p0, 17)).Error);
        Assert.Equal(RoomError.InvalidAnswer, (await room.AnswerAsync(p0, -1)).Error);
        var (otherPawn, otherDirection) = RoomTestContext.FindMove(await room.GetSnapshotAsync(), 0);
        Assert.Equal(RoomError.QuestionPending, (await room.BeginMoveAsync(p0, otherPawn, otherDirection)).Error);
    }

    [Fact]
    public async Task ClientSendsOnlyAnIndexAndServerDecidesTheOutcome()
    {
        var context = new RoomTestContext();
        var (room, p0, _) = await context.StartTwoPlayerGameAsync();
        var (movedPawn, _) = await RoomTestContext.BeginAnyMoveAsync(room, p0, 0);
        var before = (await room.GetSnapshotAsync()).Game!.Pawns.Single(pawn => pawn.Id == movedPawn);

        var correct = await room.AnswerAsync(p0, Right);

        Assert.True(correct.Value!.WasCorrect);
        Assert.Equal(MoveOutcome.Moved, correct.Value.Outcome);
        var after = (await room.GetSnapshotAsync()).Game!.Pawns.Single(pawn => pawn.Id == movedPawn);
        Assert.NotEqual((before.Row, before.Column), (after.Row, after.Column));
        Assert.True(after.HasActedThisTurn);

        var (stayedPawn, _) = await RoomTestContext.BeginAnyMoveAsync(room, p0, 0);
        var positionBefore = (await room.GetSnapshotAsync()).Game!.Pawns.Single(pawn => pawn.Id == stayedPawn);
        var wrong = await room.AnswerAsync(p0, Wrong);

        Assert.Equal(MoveOutcome.Stayed, wrong.Value!.Outcome);
        var positionAfter = (await room.GetSnapshotAsync()).Game!.Pawns.Single(pawn => pawn.Id == stayedPawn);
        Assert.Equal((positionBefore.Row, positionBefore.Column), (positionAfter.Row, positionAfter.Column));
        Assert.True(positionAfter.HasActedThisTurn);
    }

    [Fact]
    public async Task QuestionDtoDoesNotExposeTheCorrectAnswer()
    {
        var names = typeof(QuestionDto).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name);
        Assert.DoesNotContain(names, name => name.Contains("Correct", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Explanation", StringComparison.OrdinalIgnoreCase) || name.Contains("Answer", StringComparison.OrdinalIgnoreCase));

        var context = new RoomTestContext();
        var (room, p0, _) = await context.StartTwoPlayerGameAsync();
        var (_, question) = await RoomTestContext.BeginAnyMoveAsync(room, p0, 0);
        var json = JsonSerializer.Serialize(question);
        Assert.DoesNotContain("correct", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Because", json);
        Assert.Equal(4, question.Options.Count);
        Assert.Equal(context.Time.GetUtcNow().AddSeconds(30), question.ExpiresAtUtc);
        Assert.Equal(question.QuestionId, (await room.GetPendingQuestionAsync(p0))!.QuestionId);
        Assert.Null(await room.GetPendingQuestionAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task TimeoutAfterThirtySecondsCountsAsIncorrectAnswer()
    {
        var context = new RoomTestContext();
        var (room, p0, _) = await context.StartTwoPlayerGameAsync();
        var (pawnId, _) = await RoomTestContext.BeginAnyMoveAsync(room, p0, 0);
        var before = (await room.GetSnapshotAsync()).Game!.Pawns.Single(pawn => pawn.Id == pawnId);
        var events = new List<RoomEvent>();
        using var subscription = room.Subscribe(events.Add);

        context.Time.Advance(TimeSpan.FromSeconds(29));
        await room.WaitForTimeoutHandlingAsync();
        Assert.NotNull((await room.GetSnapshotAsync()).Game!.Pending);

        context.Time.Advance(TimeSpan.FromSeconds(1));
        await room.WaitForTimeoutHandlingAsync();

        var game = (await room.GetSnapshotAsync()).Game!;
        var after = game.Pawns.Single(pawn => pawn.Id == pawnId);
        Assert.Null(game.Pending);
        Assert.Equal((before.Row, before.Column), (after.Row, after.Column));
        Assert.True(after.HasActedThisTurn);
        Assert.Contains(events, roomEvent => roomEvent is { Type: RoomEventType.ActionResolved, Outcome: MoveOutcome.Stayed });
        Assert.Equal(RoomError.NoPendingQuestion, (await room.AnswerAsync(p0, Right)).Error);
    }

    [Fact]
    public async Task AnswerAfterDeadlineIsIncorrectEvenIfTimerHasNotBeenProcessed()
    {
        var context = new RoomTestContext();
        var (room, p0, _) = await context.StartTwoPlayerGameAsync();
        await RoomTestContext.BeginAnyMoveAsync(room, p0, 0);
        context.Time.SetUtcNow(context.Time.GetUtcNow().AddSeconds(30));
        await room.WaitForTimeoutHandlingAsync();

        Assert.Equal(RoomError.NoPendingQuestion, (await room.AnswerAsync(p0, Right)).Error);
    }

    [Fact]
    public async Task DuelTimeoutAbortsDuelAndEachQuestionGetsItsOwnThirtySeconds()
    {
        var context = new RoomTestContext();
        var (room, _, p0, _, attackerId, defenderId) = await context.ArrangeDuelAsync();

        var first = await room.BeginMoveAsync(p0, attackerId, Direction.Right);
        Assert.True(first.Value!.IsDuel);
        Assert.Equal((1, 3), (first.Value.Number, first.Value.Total));

        context.Time.Advance(TimeSpan.FromSeconds(20));
        var second = await room.AnswerAsync(p0, Right);
        Assert.False(second.Value!.IsCompleted);
        Assert.Equal(2, second.Value.NextQuestion!.Number);

        context.Time.Advance(TimeSpan.FromSeconds(20));
        await room.WaitForTimeoutHandlingAsync();
        Assert.NotNull((await room.GetSnapshotAsync()).Game!.Pending);

        context.Time.Advance(TimeSpan.FromSeconds(10));
        await room.WaitForTimeoutHandlingAsync();

        var game = (await room.GetSnapshotAsync()).Game!;
        Assert.Null(game.Pending);
        Assert.False(game.Pawns.Single(pawn => pawn.Id == defenderId).IsCaptured);
        var attacker = game.Pawns.Single(pawn => pawn.Id == attackerId);
        Assert.Equal((0, 1), (attacker.Row, attacker.Column));
        Assert.True(attacker.HasActedThisTurn);
    }

    [Fact]
    public async Task WinningDuelCapturesPawnAndEmitsEvents()
    {
        var context = new RoomTestContext();
        var (room, _, p0, _, attackerId, defenderId) = await context.ArrangeDuelAsync();
        var events = new List<RoomEvent>();
        using var subscription = room.Subscribe(events.Add);

        await room.BeginMoveAsync(p0, attackerId, Direction.Right);
        await room.AnswerAsync(p0, Right);
        await room.AnswerAsync(p0, Right);
        var last = await room.AnswerAsync(p0, Right);

        Assert.Equal(MoveOutcome.Captured, last.Value!.Outcome);
        var game = (await room.GetSnapshotAsync()).Game!;
        Assert.True(game.Pawns.Single(pawn => pawn.Id == defenderId).IsCaptured);
        var attacker = game.Pawns.Single(pawn => pawn.Id == attackerId);
        Assert.Equal((0, 2), (attacker.Row, attacker.Column));
        Assert.Contains(events, roomEvent => roomEvent.Type == RoomEventType.PawnCaptured && roomEvent.PawnId == defenderId);
        Assert.Contains(events, roomEvent => roomEvent.Type == RoomEventType.PawnMoved && roomEvent.PawnId == attackerId);
    }

    [Fact]
    public async Task WrongAnswerInDuelStopsItAtOnce()
    {
        var context = new RoomTestContext();
        var (room, _, p0, _, attackerId, defenderId) = await context.ArrangeDuelAsync();

        await room.BeginMoveAsync(p0, attackerId, Direction.Right);
        var result = await room.AnswerAsync(p0, Wrong);

        Assert.True(result.Value!.IsCompleted);
        Assert.Equal(MoveOutcome.Stayed, result.Value.Outcome);
        Assert.False((await room.GetSnapshotAsync()).Game!.Pawns.Single(pawn => pawn.Id == defenderId).IsCaptured);
    }

    [Fact]
    public async Task CapturingLastPawnFinishesGameAndRoomIsNoLongerRecovered()
    {
        var context = new RoomTestContext();
        var (room, _, p0, _, attackerId, _) = await context.ArrangeDuelAsync(lastDefender: true);
        var events = new List<RoomEvent>();
        using var subscription = room.Subscribe(events.Add);

        await room.BeginMoveAsync(p0, attackerId, Direction.Right);
        await room.AnswerAsync(p0, Right);
        await room.AnswerAsync(p0, Right);
        await room.AnswerAsync(p0, Right);

        Assert.Equal(RoomState.Finished, room.State);
        var snapshot = await room.GetSnapshotAsync();
        Assert.Equal(0, snapshot.Game!.WinnerIndex);
        Assert.Equal(GameState.Finished, snapshot.Game.State);
        Assert.Contains(events, roomEvent => roomEvent.Type == RoomEventType.GameFinished && roomEvent.PlayerIndex == 0);
        Assert.Empty(await context.Store.LoadActiveAsync());
        Assert.Equal(RoomError.NotInProgress, (await room.BeginMoveAsync(p0, attackerId, Direction.Right)).Error);
    }

    [Fact]
    public async Task TurnChangedIsRaisedWhenAllPawnsHaveActed()
    {
        var context = new RoomTestContext();
        var (room, p0, _) = await context.StartTwoPlayerGameAsync();
        var events = new List<RoomEvent>();
        using var subscription = room.Subscribe(events.Add);

        for (var i = 0; i < 3; i++)
        {
            await RoomTestContext.BeginAnyMoveAsync(room, p0, 0);
            Assert.True((await room.AnswerAsync(p0, Right)).Success);
        }

        Assert.Equal(3, events.Count(roomEvent => roomEvent.Type == RoomEventType.PawnMoved));
        var turn = Assert.Single(events, roomEvent => roomEvent.Type == RoomEventType.TurnChanged);
        Assert.Equal(1, turn.PlayerIndex);
        Assert.Equal(1, (await room.GetSnapshotAsync()).Game!.CurrentPlayerIndex);
    }

    [Fact]
    public async Task NpcPlaysItsTurnAutomaticallyAndReturnsControlToHuman()
    {
        var context = new RoomTestContext();
        var host = Guid.NewGuid();
        var room = (await context.Manager.CreateRoomAsync(host, "Ana", 10, seed: 7)).Value!;
        await context.Manager.AddNpcAsync(room.Code, host);
        await context.Manager.StartGameAsync(room.Code, host);
        var start = (await room.GetSnapshotAsync()).Game!.Pawns.Where(pawn => pawn.OwnerIndex == 1)
            .Select(pawn => (pawn.Id, pawn.Row, pawn.Column)).ToList();

        for (var i = 0; i < 3; i++)
        {
            await RoomTestContext.BeginAnyMoveAsync(room, host, 0);
            Assert.True((await room.AnswerAsync(host, Right)).Success);
        }

        var game = (await room.GetSnapshotAsync()).Game!;
        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.NotEqual(start, game.Pawns.Where(pawn => pawn.OwnerIndex == 1).Select(pawn => (pawn.Id, pawn.Row, pawn.Column)).ToList());
    }

    [Fact]
    public async Task OnlyOneOfManyConcurrentMovesAndAnswersGoesThrough()
    {
        var context = new RoomTestContext();
        var (room, p0, _) = await context.StartTwoPlayerGameAsync();
        var (pawnId, direction) = RoomTestContext.FindMove(await room.GetSnapshotAsync(), 0);

        var begins = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => room.BeginMoveAsync(p0, pawnId, direction))));
        Assert.Equal(1, begins.Count(result => result.Success));
        Assert.All(begins.Where(result => !result.Success), result => Assert.Equal(RoomError.QuestionPending, result.Error));

        var answers = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => room.AnswerAsync(p0, Right))));
        Assert.Equal(1, answers.Count(result => result.Success));
    }
}
