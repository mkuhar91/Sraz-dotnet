using Sraz.Application.Abstractions;
using Sraz.Application.Rooms;
using Sraz.Domain.Models;
using Xunit;

namespace Sraz.Tests;

public sealed class Phase4RoomTests
{
    [Fact]
    public async Task DisconnectIsPersistedAndTakeoverHappensAtSixtySeconds()
    {
        var context = new RoomTestContext();
        var (room, _, guest) = await context.StartTwoPlayerGameAsync();
        var events = new List<RoomEvent>();
        using var subscription = room.Subscribe(events.Add);
        var activity = room.LastActivityUtc;

        Assert.True((await room.SetConnectedAsync(guest, false)).Success);
        var disconnected = context.Store.Get(room.Code)!.Players[1];
        Assert.Equal(context.Time.GetUtcNow(), disconnected.DisconnectedAtUtc);
        Assert.True((await room.GetViewAsync()).Players[1].IsDisconnected);
        context.Time.Advance(TimeSpan.FromSeconds(59));
        await room.WaitForTimeoutHandlingAsync();
        Assert.False((await room.GetSnapshotAsync()).Players[1].IsNpc);
        context.Time.Advance(TimeSpan.FromSeconds(1));
        await room.WaitForTimeoutHandlingAsync();

        var player = (await room.GetSnapshotAsync()).Players[1];
        Assert.True(player.IsNpc);
        Assert.Equal(guest, player.OriginalPlayerId);
        Assert.Equal(activity, room.LastActivityUtc);
        Assert.Contains(events, item => item.Type == RoomEventType.PlayerDisconnected);
        Assert.Contains(events, item => item.Type == RoomEventType.NpcTakeover);
    }

    [Fact]
    public async Task ReconnectWithinGraceCancelsTakeoverAndDuplicateDisconnectDoesNotExtendIt()
    {
        var context = new RoomTestContext();
        var (room, _, guest) = await context.StartTwoPlayerGameAsync();
        await room.SetConnectedAsync(guest, false);
        context.Time.Advance(TimeSpan.FromSeconds(40));
        await room.SetConnectedAsync(guest, false);
        Assert.Equal(context.Time.GetUtcNow().AddSeconds(-40), (await room.GetSnapshotAsync()).Players[1].DisconnectedAtUtc);
        await room.SetConnectedAsync(guest, true);
        context.Time.Advance(TimeSpan.FromSeconds(60));
        await room.WaitForTimeoutHandlingAsync();
        Assert.False((await room.GetSnapshotAsync()).Players[1].IsNpc);
        Assert.Null((await room.GetSnapshotAsync()).Players[1].DisconnectedAtUtc);
    }

    [Fact]
    public async Task QuestionTimeoutTakesDisconnectedCurrentPlayerOverImmediately()
    {
        var context = new RoomTestContext();
        var (room, host, _) = await context.StartTwoPlayerGameAsync();
        await RoomTestContext.BeginAnyMoveAsync(room, host, 0);
        await room.SetConnectedAsync(host, false);
        context.Time.Advance(TimeSpan.FromSeconds(30));
        await room.WaitForTimeoutHandlingAsync();

        var snapshot = await room.GetSnapshotAsync();
        Assert.True(snapshot.Players[0].IsNpc);
        Assert.Equal(host, snapshot.Players[0].OriginalPlayerId);
        Assert.Null(snapshot.Game!.Pending);
        Assert.Equal(1, snapshot.Game.CurrentPlayerIndex);
        Assert.Equal(RoomError.PlayerNotInRoom, (await room.AnswerAsync(host, 1)).Error);
    }

    [Fact]
    public async Task ReconnectedNpcWaitsForNextOwnTurn()
    {
        var context = new RoomTestContext();
        var (room, host, guest) = await context.StartTwoPlayerGameAsync();
        await room.SetConnectedAsync(guest, false);
        context.Time.Advance(TimeSpan.FromSeconds(60));
        await room.WaitForTimeoutHandlingAsync();
        await room.SetConnectedAsync(guest, true);
        Assert.True((await room.GetSnapshotAsync()).Players[1].IsNpc);
        await FinishTurnAsync(room, host, 0);

        var snapshot = await room.GetSnapshotAsync();
        Assert.Equal(1, snapshot.Game!.CurrentPlayerIndex);
        Assert.False(snapshot.Players[1].IsNpc);
        Assert.Null(snapshot.Players[1].OriginalPlayerId);
        Assert.True((await RoomTestContext.BeginAnyMoveAsync(room, guest, 1)).Question.Options.Count > 0);
    }

    [Fact]
    public async Task AllDisconnectedPlayersContinueUntilFinishedOrExpiredWithoutRefreshingActivity()
    {
        var context = new RoomTestContext();
        var (room, host, guest) = await context.StartTwoPlayerGameAsync();
        var activity = room.LastActivityUtc;
        await room.SetConnectedAsync(host, false);
        await room.SetConnectedAsync(guest, false);
        var beforeTakeover = context.Store.SaveCount;
        context.Time.Advance(TimeSpan.FromSeconds(60));
        await room.WaitForTimeoutHandlingAsync();
        var saves = context.Store.SaveCount;
        Assert.True(saves > beforeTakeover + 2);
        Assert.All((await room.GetSnapshotAsync()).Players, player => Assert.True(player.IsNpc));
        context.Time.Advance(TimeSpan.FromHours(3));
        await room.WaitForTimeoutHandlingAsync();
        Assert.Equal(saves, context.Store.SaveCount);
        Assert.Equal(activity, room.LastActivityUtc);
        Assert.Equal(1, await context.Manager.RemoveInactiveAsync());
    }

    [Fact]
    public async Task FailedPresenceWritesAndTakeoverRollBackAndRetry()
    {
        var context = new RoomTestContext();
        var (room, _, guest) = await context.StartTwoPlayerGameAsync();
        context.Store.FailWrites = true;
        Assert.Equal(RoomError.PersistenceFailed, (await room.SetConnectedAsync(guest, false)).Error);
        Assert.Null((await room.GetSnapshotAsync()).Players[1].DisconnectedAtUtc);
        context.Store.FailWrites = false;
        await room.SetConnectedAsync(guest, false);
        context.Store.FailWrites = true;
        context.Time.Advance(TimeSpan.FromSeconds(60));
        await room.WaitForTimeoutHandlingAsync();
        Assert.False((await room.GetSnapshotAsync()).Players[1].IsNpc);
        context.Store.FailWrites = false;
        context.Time.Advance(TimeSpan.FromSeconds(1));
        await room.WaitForTimeoutHandlingAsync();
        Assert.True((await room.GetSnapshotAsync()).Players[1].IsNpc);
    }

    [Fact]
    public async Task RecoveryPreservesDisconnectDeadlineAndGameIdentity()
    {
        var context = new RoomTestContext();
        var (room, _, guest) = await context.StartTwoPlayerGameAsync();
        await room.SetConnectedAsync(guest, false);
        var expected = await room.GetSnapshotAsync();
        room.Close();
        context.Time.Advance(TimeSpan.FromSeconds(45));
        var manager = context.NewManagerOnSameStore();
        await manager.LoadAsync();
        var restored = manager.GetRoom(room.Code)!;
        Assert.Equal(expected.GameId, (await restored.GetSnapshotAsync()).GameId);
        context.Time.Advance(TimeSpan.FromSeconds(15));
        await restored.WaitForTimeoutHandlingAsync();
        Assert.True((await restored.GetSnapshotAsync()).Players[1].IsNpc);
        Assert.Equal(guest, (await restored.GetSnapshotAsync()).Players[1].OriginalPlayerId);
    }

    [Fact]
    public async Task VersionOneSnapshotsRemainSupported()
    {
        var context = new RoomTestContext();
        var (room, _, _) = await context.StartTwoPlayerGameAsync();
        var snapshot = await room.GetSnapshotAsync();
        room.Close();
        snapshot.SchemaVersion = 1;
        snapshot.GameId = null;
        await context.Store.SaveAsync(snapshot);
        var manager = context.NewManagerOnSameStore();
        Assert.Equal(1, await manager.LoadAsync());
        var restored = await manager.GetRoom(room.Code)!.GetSnapshotAsync();
        Assert.Equal(2, restored.SchemaVersion);
        Assert.NotNull(restored.GameId);
    }

    [Fact]
    public async Task FinishRecordsResultAndHostCanRestartSamePlayersWithNewGameId()
    {
        var context = new RoomTestContext();
        var (room, _, host, guest, attacker, _) = await context.ArrangeDuelAsync(lastDefender: true);
        var gameId = (await room.GetSnapshotAsync()).GameId;
        Assert.Equal(RoomError.NotInLobby, (await room.RestartAsync(host)).Error);
        await room.BeginMoveAsync(host, attacker, Direction.Right);
        for (var i = 0; i < 3; i++)
        {
            await room.AnswerAsync(host, TestQuestionProvider.CorrectIndex);
        }

        var finished = context.Store.Get(room.Code)!;
        Assert.Equal(gameId, finished.Result!.GameId);
        Assert.Equal(host, Assert.Single(finished.Result.Players, player => player.Won).PlayerId);
        Assert.Equal(RoomError.NotHost, (await room.RestartAsync(guest)).Error);
        context.Store.FailWrites = true;
        Assert.Equal(RoomError.PersistenceFailed, (await room.RestartAsync(host)).Error);
        Assert.Equal(RoomState.Finished, room.State);
        context.Store.FailWrites = false;
        Assert.True((await room.RestartAsync(host)).Success);
        var lobby = await room.GetSnapshotAsync();
        Assert.Equal(RoomState.Lobby, lobby.State);
        Assert.Equal(new[] { host, guest }, lobby.Players.Select(player => player.Id));
        Assert.Null(lobby.Game);
        Assert.Null(lobby.Result);
        Assert.Null(lobby.GameId);
        Assert.True((await room.StartGameAsync(host)).Success);
        Assert.NotEqual(gameId, (await room.GetSnapshotAsync()).GameId);
    }

    [Fact]
    public async Task NpcDriveIsBoundedAndReturningDuringCurrentNpcTurnDoesNotRestoreEarly()
    {
        var context = new RoomTestContext();
        var (original, host, guest) = await context.StartTwoPlayerGameAsync();
        var snapshot = await original.GetSnapshotAsync();
        original.Close();
        snapshot.Players[0].IsNpc = true;
        snapshot.Players[0].OriginalPlayerId = host;
        snapshot.Players[0].DisconnectedAtUtc = context.Time.GetUtcNow();
        var services = new RoomServices(context.Store, new TestQuestionProvider(), new BlockedNpc(),
            context.Time, TimeSpan.FromSeconds(30));
        using var room = await GameRoom.RestoreAsync(snapshot, services);
        var before = context.Store.SaveCount;
        Assert.True((await room.SetConnectedAsync(host, true)).Success);
        var after = await room.GetSnapshotAsync();
        Assert.True(after.Players[0].IsNpc);
        Assert.Equal(0, after.Game!.CurrentPlayerIndex);
        Assert.InRange(context.Store.SaveCount - before, 1, 65);
        Assert.Empty((await room.GetViewAsync(host)).AllowedMoves);
        Assert.Equal(RoomError.PlayerNotInRoom, (await room.AnswerAsync(Guid.NewGuid(), 0)).Error);
        Assert.Equal(RoomError.PlayerNotInRoom, (await room.SetConnectedAsync(Guid.NewGuid(), true)).Error);
    }

    [Fact]
    public async Task TakeoverWinnerGetsNoHumanWinAndRestartRestoresOriginalSeat()
    {
        var context = new RoomTestContext();
        var (original, _, host, guest, attacker, _) = await context.ArrangeDuelAsync(lastDefender: true);
        var snapshot = await original.GetSnapshotAsync();
        original.Close();
        snapshot.Players[0].IsNpc = true;
        snapshot.Players[0].OriginalPlayerId = host;
        snapshot.Players[0].DisconnectedAtUtc = context.Time.GetUtcNow();
        var services = new RoomServices(context.Store, new TestQuestionProvider(), new CapturingNpc(attacker),
            context.Time, TimeSpan.FromSeconds(30));
        using var room = await GameRoom.RestoreAsync(snapshot, services);

        var finished = await room.GetSnapshotAsync();
        Assert.Equal(RoomState.Finished, finished.State);
        Assert.Equal(0, finished.Game!.WinnerIndex);
        Assert.DoesNotContain(finished.Result!.Players, player => player.Won);
        Assert.Contains(finished.Result.Players, player => player.PlayerId == host);
        Assert.Contains(finished.Result.Players, player => player.PlayerId == guest);
        Assert.True((await room.RestartAsync(host)).Success);
        var lobby = await room.GetSnapshotAsync();
        Assert.False(lobby.Players[0].IsNpc);
        Assert.Null(lobby.Players[0].OriginalPlayerId);
        Assert.Equal(host, lobby.Players[0].Id);
    }

    [Fact]
    public async Task RestoreAfterGracePeriodTakesOverImmediatelyAndPreservesOriginalIdentity()
    {
        var context = new RoomTestContext();
        var (room, _, guest) = await context.StartTwoPlayerGameAsync();
        await room.SetConnectedAsync(guest, false);
        room.Close();
        context.Time.Advance(TimeSpan.FromSeconds(75));
        var manager = context.NewManagerOnSameStore();
        await manager.LoadAsync();
        var restored = manager.GetRoom(room.Code)!;
        var player = (await restored.GetSnapshotAsync()).Players[1];
        Assert.True(player.IsNpc);
        Assert.Equal(guest, player.OriginalPlayerId);
        Assert.Equal(context.Time.GetUtcNow().AddSeconds(-75), player.DisconnectedAtUtc);
        restored.Close();
        var recoveredAgain = context.NewManagerOnSameStore();
        await recoveredAgain.LoadAsync();
        Assert.Equal(guest, (await recoveredAgain.GetRoom(room.Code)!.GetSnapshotAsync()).Players[1].OriginalPlayerId);
    }

    [Fact]
    public async Task FailedTimeoutTakeoverKeepsQuestionAndIdentityUntilSuccessfulRetry()
    {
        var context = new RoomTestContext();
        var (room, host, _) = await context.StartTwoPlayerGameAsync();
        await RoomTestContext.BeginAnyMoveAsync(room, host, 0);
        await room.SetConnectedAsync(host, false);
        var events = new List<RoomEvent>();
        using var subscription = room.Subscribe(events.Add);
        context.Store.FailWrites = true;
        context.Time.Advance(TimeSpan.FromSeconds(30));
        await room.WaitForTimeoutHandlingAsync();
        var failed = await room.GetSnapshotAsync();
        Assert.False(failed.Players[0].IsNpc);
        Assert.Null(failed.Players[0].OriginalPlayerId);
        Assert.NotNull(failed.Game!.Pending);
        Assert.DoesNotContain(events, item => item.Type == RoomEventType.NpcTakeover);
        context.Store.FailWrites = false;
        context.Time.Advance(TimeSpan.FromSeconds(30));
        await room.WaitForTimeoutHandlingAsync();
        Assert.True((await room.GetSnapshotAsync()).Players[0].IsNpc);
        Assert.Null((await room.GetSnapshotAsync()).Game!.Pending);
        Assert.Contains(events, item => item.Type == RoomEventType.NpcTakeover);
    }

    [Fact]
    public async Task EliminatedOriginalHumansDoNotKeepPermanentNpcGameRunning()
    {
        var context = new RoomTestContext();
        var host = Guid.NewGuid();
        var original = (await context.Manager.CreateRoomAsync(host, "Ana", 6, seed: 42)).Value!;
        await original.AddNpcAsync(host);
        await original.AddNpcAsync(host);
        await original.StartGameAsync(host);
        var snapshot = await original.GetSnapshotAsync();
        original.Close();
        snapshot.Players[0].IsNpc = true;
        snapshot.Players[0].OriginalPlayerId = host;
        snapshot.Players[0].DisconnectedAtUtc = null;
        foreach (var pawn in snapshot.Game!.Pawns.Where(pawn => pawn.OwnerIndex == 0))
        {
            pawn.IsCaptured = true;
        }
        snapshot.Game.CurrentPlayerIndex = 1;
        var before = context.Store.SaveCount;
        var services = new RoomServices(context.Store, new TestQuestionProvider(), new BlockedNpc(),
            context.Time, TimeSpan.FromSeconds(30));
        using var room = await GameRoom.RestoreAsync(snapshot, services);
        context.Time.Advance(TimeSpan.FromMinutes(5));
        await room.WaitForTimeoutHandlingAsync();
        Assert.Equal(before, context.Store.SaveCount);
        Assert.Equal(1, (await room.GetSnapshotAsync()).Game!.CurrentPlayerIndex);
    }

    [Fact]
    public async Task ProductionRecoveryDisconnectsHumansAndPreservesEarlierDisconnectTimes()
    {
        var context = new RoomTestContext();
        var (original, _, guest) = await context.StartTwoPlayerGameAsync();
        await original.SetConnectedAsync(guest, false);
        var earlier = context.Time.GetUtcNow();
        var activity = original.LastActivityUtc;
        original.Close();
        context.Time.Advance(TimeSpan.FromSeconds(20));
        var manager = context.NewManagerOnSameStore();
        Assert.Equal(1, await manager.LoadAsync(disconnectPlayers: true));
        var restored = await manager.GetRoom(original.Code)!.GetSnapshotAsync();
        Assert.Equal(context.Time.GetUtcNow(), restored.Players[0].DisconnectedAtUtc);
        Assert.Equal(earlier, restored.Players[1].DisconnectedAtUtc);
        Assert.Equal(activity, restored.LastActivityUtc);
        Assert.Equal(restored.Players[0].DisconnectedAtUtc,
            context.Store.Get(original.Code)!.Players[0].DisconnectedAtUtc);
    }

    private sealed class CapturingNpc(Guid pawnId) : INpcStrategy
    {
        public (Pawn Pawn, Direction Direction)? ChooseMove(IGameEngine engine) =>
            (engine.CurrentPlayer.ActivePawns.Single(pawn => pawn.Id == pawnId), Direction.Right);
        public bool AnswerQuestionCorrectly() => true;
    }

    private sealed class BlockedNpc : INpcStrategy
    {
        public (Pawn Pawn, Direction Direction)? ChooseMove(IGameEngine engine) =>
            (engine.CurrentPlayer.ActivePawns[0], Direction.Up);
        public bool AnswerQuestionCorrectly() => false;
    }

    private static async Task FinishTurnAsync(GameRoom room, Guid player, int index)
    {
        for (var i = 0; i < 3; i++)
        {
            await RoomTestContext.BeginAnyMoveAsync(room, player, index);
            Assert.True((await room.AnswerAsync(player, TestQuestionProvider.CorrectIndex)).Success);
        }
    }
}
