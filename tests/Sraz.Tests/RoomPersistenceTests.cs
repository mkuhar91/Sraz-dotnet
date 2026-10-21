using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sraz.Application.Rooms;
using Sraz.Domain.Models;
using Sraz.Infrastructure.Persistence;
using Xunit;

namespace Sraz.Tests;

public sealed class RoomPersistenceTests
{
    private const int Right = TestQuestionProvider.CorrectIndex;

    [Fact]
    public async Task SnapshotIsSavedAfterEveryStepAndMatchesMemory()
    {
        var context = new RoomTestContext();
        var host = Guid.NewGuid();
        var room = (await context.Manager.CreateRoomAsync(host, "Ana", 6, seed: 5)).Value!;
        await AssertStoredMatchesAsync(context, room);

        var saves = context.Store.SaveCount;
        await context.Manager.JoinAsync(room.Code, Guid.NewGuid(), "Ben");
        Assert.Equal(++saves, context.Store.SaveCount);
        await context.Manager.AddNpcAsync(room.Code, host);
        Assert.Equal(++saves, context.Store.SaveCount);
        var npcId = (await room.GetSnapshotAsync()).Players.Single(player => player.IsNpc).Id;
        await context.Manager.RemoveNpcAsync(room.Code, host, npcId);
        Assert.Equal(++saves, context.Store.SaveCount);
        await context.Manager.StartGameAsync(room.Code, host);
        Assert.Equal(++saves, context.Store.SaveCount);
        await AssertStoredMatchesAsync(context, room);

        await RoomTestContext.BeginAnyMoveAsync(room, host, 0);
        Assert.Equal(++saves, context.Store.SaveCount);
        Assert.NotNull(context.Store.Get(room.Code)!.Game!.Pending);
        await room.AnswerAsync(host, Right);
        Assert.Equal(++saves, context.Store.SaveCount);
        await AssertStoredMatchesAsync(context, room);

        var stored = context.Store.Get(room.Code)!;
        Assert.Equal(RoomSnapshot.CurrentSchemaVersion, stored.SchemaVersion);
        Assert.Equal(5, stored.Seed);
        Assert.Equal(6, stored.BoardSize);
        Assert.Equal(6, stored.Game!.Pawns.Count);
        Assert.Contains(stored.Game.Pawns, pawn => pawn.HasActedThisTurn);
    }

    [Fact]
    public async Task LoadingSnapshotRestoresIdenticalState()
    {
        var context = new RoomTestContext();
        var (room, p0, p1) = await context.StartTwoPlayerGameAsync();
        for (var i = 0; i < 4; i++)
        {
            var player = i < 3 ? p0 : p1;
            await RoomTestContext.BeginAnyMoveAsync(room, player, i < 3 ? 0 : 1);
            await room.AnswerAsync(player, i % 2 == 0 ? Right : 0);
        }

        var expected = RoomSnapshotSerializer.Serialize(await room.GetSnapshotAsync());
        room.Close();

        var recovered = context.NewManagerOnSameStore();
        Assert.Equal(1, await recovered.LoadAsync());

        var restored = recovered.GetRoom(room.Code)!;
        Assert.Equal(expected, RoomSnapshotSerializer.Serialize(await restored.GetSnapshotAsync()));
        var snapshot = await restored.GetSnapshotAsync();
        Assert.Equal(1, snapshot.Game!.CurrentPlayerIndex);
        Assert.Contains(snapshot.Game.Pawns, pawn => pawn is { OwnerIndex: 1, HasActedThisTurn: true });
        Assert.True((await restored.JoinAsync(p0, "Ana")).Success);
        var (pawnId, direction) = RoomTestContext.FindMove(snapshot, 1);
        Assert.True((await restored.BeginMoveAsync(p1, pawnId, direction)).Success);
    }

    [Fact]
    public async Task CapturedPawnsSurviveRoundTrip()
    {
        var context = new RoomTestContext();
        var (room, _, p0, _, attackerId, defenderId) = await context.ArrangeDuelAsync();
        await room.BeginMoveAsync(p0, attackerId, Direction.Right);
        await room.AnswerAsync(p0, Right);
        await room.AnswerAsync(p0, Right);
        await room.AnswerAsync(p0, Right);
        var expected = RoomSnapshotSerializer.Serialize(await room.GetSnapshotAsync());
        room.Close();

        var recovered = context.NewManagerOnSameStore();
        await recovered.LoadAsync();

        var snapshot = await recovered.GetRoom(room.Code)!.GetSnapshotAsync();
        Assert.Equal(expected, RoomSnapshotSerializer.Serialize(snapshot));
        Assert.True(snapshot.Game!.Pawns.Single(pawn => pawn.Id == defenderId).IsCaptured);
    }

    [Fact]
    public async Task RestartInTheMiddleOfAQuestionCountsItAsIncorrect()
    {
        var context = new RoomTestContext();
        var (room, p0, _) = await context.StartTwoPlayerGameAsync();
        var (pawnId, _) = await RoomTestContext.BeginAnyMoveAsync(room, p0, 0);
        var before = (await room.GetSnapshotAsync()).Game!.Pawns.Single(pawn => pawn.Id == pawnId);
        room.Close();

        var recovered = context.NewManagerOnSameStore();
        await recovered.LoadAsync();

        var game = (await recovered.GetRoom(room.Code)!.GetSnapshotAsync()).Game!;
        var after = game.Pawns.Single(pawn => pawn.Id == pawnId);
        Assert.Null(game.Pending);
        Assert.Equal((before.Row, before.Column), (after.Row, after.Column));
        Assert.True(after.HasActedThisTurn);
        Assert.Null(context.Store.Get(room.Code)!.Game!.Pending);
    }

    [Fact]
    public async Task RestartInTheMiddleOfADuelEndsTheDuelAsIncorrect()
    {
        var context = new RoomTestContext();
        var (room, manager, p0, _, attackerId, defenderId) = await context.ArrangeDuelAsync();
        await room.BeginMoveAsync(p0, attackerId, Direction.Right);
        await room.AnswerAsync(p0, Right);
        room.Close();

        var recovered = context.NewManagerOnSameStore();
        await recovered.LoadAsync();

        var game = (await recovered.GetRoom(room.Code)!.GetSnapshotAsync()).Game!;
        Assert.False(game.Pawns.Single(pawn => pawn.Id == defenderId).IsCaptured);
        Assert.True(game.Pawns.Single(pawn => pawn.Id == attackerId).HasActedThisTurn);
        Assert.Null(game.Pending);
        Assert.Equal(1, manager.RoomCount);
    }

    [Fact]
    public async Task FailedWriteMeansTheMoveIsNotApplied()
    {
        var context = new RoomTestContext();
        var (room, p0, _) = await context.StartTwoPlayerGameAsync();
        var (pawnId, direction) = RoomTestContext.FindMove(await room.GetSnapshotAsync(), 0);

        context.Store.FailWrites = true;
        Assert.Equal(RoomError.PersistenceFailed, (await room.BeginMoveAsync(p0, pawnId, direction)).Error);
        context.Store.FailWrites = false;
        Assert.Null((await room.GetSnapshotAsync()).Game!.Pending);

        Assert.True((await room.BeginMoveAsync(p0, pawnId, direction)).Success);
        var beforeAnswer = RoomSnapshotSerializer.Serialize(await room.GetSnapshotAsync());

        context.Store.FailWrites = true;
        Assert.Equal(RoomError.PersistenceFailed, (await room.AnswerAsync(p0, Right)).Error);
        context.Store.FailWrites = false;

        Assert.Equal(beforeAnswer, RoomSnapshotSerializer.Serialize(await room.GetSnapshotAsync()));
        var retry = await room.AnswerAsync(p0, Right);
        Assert.Equal(MoveOutcome.Moved, retry.Value!.Outcome);
        await AssertStoredMatchesAsync(context, room);
    }

    [Fact]
    public async Task FailedWriteRollsBackLobbyAndStartChanges()
    {
        var context = new RoomTestContext();
        var host = Guid.NewGuid();
        var room = (await context.Manager.CreateRoomAsync(host, "Ana")).Value!;
        await context.Manager.AddNpcAsync(room.Code, host);

        context.Store.FailWrites = true;
        Assert.Equal(RoomError.PersistenceFailed, (await context.Manager.JoinAsync(room.Code, Guid.NewGuid(), "Ben")).Error);
        Assert.Equal(RoomError.PersistenceFailed, (await context.Manager.AddNpcAsync(room.Code, host)).Error);
        Assert.Equal(RoomError.PersistenceFailed, (await context.Manager.StartGameAsync(room.Code, host)).Error);

        var snapshot = await room.GetSnapshotAsync();
        Assert.Equal(2, snapshot.Players.Count);
        Assert.Equal(RoomState.Lobby, room.State);
        Assert.Null(snapshot.Game);

        context.Store.FailWrites = false;
        Assert.True((await context.Manager.StartGameAsync(room.Code, host)).Success);
    }

    [Fact]
    public async Task FailedWriteDuringTimeoutKeepsQuestionOpenWithNewTimer()
    {
        var context = new RoomTestContext();
        var (room, p0, _) = await context.StartTwoPlayerGameAsync();
        await RoomTestContext.BeginAnyMoveAsync(room, p0, 0);

        context.Store.FailWrites = true;
        context.Time.Advance(TimeSpan.FromSeconds(30));
        await room.WaitForTimeoutHandlingAsync();
        context.Store.FailWrites = false;
        Assert.NotNull((await room.GetSnapshotAsync()).Game!.Pending);

        context.Time.Advance(TimeSpan.FromSeconds(30));
        await room.WaitForTimeoutHandlingAsync();
        Assert.Null((await room.GetSnapshotAsync()).Game!.Pending);
    }

    [Fact]
    public async Task EfRoomStoreSavesLoadsUpdatesAndDeletes()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<SrazDbContext>(options => options.UseInMemoryDatabase(dbName));
        await using var provider = services.BuildServiceProvider();
        var store = new EfRoomStore(provider.GetRequiredService<IServiceScopeFactory>());
        var context = new RoomTestContext(store: null);
        var manager = new RoomManager(store, new TestQuestionProvider(), new Sraz.Application.Services.RandomNpcStrategy(1.0, 1), context.Time);
        var host = Guid.NewGuid();
        var room = (await manager.CreateRoomAsync(host, "Ana", 6, seed: 3)).Value!;
        await manager.AddNpcAsync(room.Code, host);
        await manager.StartGameAsync(room.Code, host);
        await RoomTestContext.BeginAnyMoveAsync(room, host, 0);
        await room.AnswerAsync(host, Right);
        var expected = RoomSnapshotSerializer.Serialize(await room.GetSnapshotAsync());
        room.Close();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
            Assert.Equal(1, await db.GameSnapshots.CountAsync());
            Assert.Equal("InProgress", (await db.GameRooms.SingleAsync()).State);
        }

        var recovered = new RoomManager(store, new TestQuestionProvider(), new Sraz.Application.Services.RandomNpcStrategy(1.0, 1), context.Time);
        Assert.Equal(1, await recovered.LoadAsync());
        Assert.Equal(expected, RoomSnapshotSerializer.Serialize(await recovered.GetRoom(room.Code)!.GetSnapshotAsync()));

        await store.DeleteAsync(room.Code);
        Assert.Empty(await store.LoadActiveAsync());
    }

    private static async Task AssertStoredMatchesAsync(RoomTestContext context, GameRoom room) =>
        Assert.Equal(RoomSnapshotSerializer.Serialize(await room.GetSnapshotAsync()), context.Store.GetJson(room.Code));
}
