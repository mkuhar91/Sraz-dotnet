using Sraz.Application.Rooms;
using Xunit;

namespace Sraz.Tests;

public sealed class RoomManagerTests
{
    [Fact]
    public async Task CreateRoomGivesRandomCodeAndHostInLobby()
    {
        var context = new RoomTestContext();
        var host = Guid.NewGuid();

        var first = await context.Manager.CreateRoomAsync(host, "Ana");
        var second = await context.Manager.CreateRoomAsync(host, "Ana");

        Assert.True(first.Success);
        Assert.Equal(5, first.Value!.Code.Length);
        Assert.NotEqual(first.Value.Code, second.Value!.Code);
        Assert.Equal(RoomState.Lobby, first.Value.State);
        Assert.Same(first.Value, context.Manager.GetRoom(first.Value.Code.ToLowerInvariant()));
        var snapshot = await first.Value.GetSnapshotAsync();
        Assert.Equal(host, snapshot.HostPlayerId);
        Assert.Single(snapshot.Players);
        Assert.NotNull(context.Store.Get(first.Value.Code));
    }

    [Fact]
    public async Task JoinAddsPlayerAndUnknownCodeIsRejected()
    {
        var context = new RoomTestContext();
        var room = (await context.Manager.CreateRoomAsync(Guid.NewGuid(), "Ana")).Value!;
        var joiner = Guid.NewGuid();

        var joined = await context.Manager.JoinAsync(room.Code, joiner, "Ben");
        var missing = await context.Manager.JoinAsync("ZZZZZ", joiner, "Ben");

        Assert.True(joined.Success);
        Assert.Equal(2, (await room.GetSnapshotAsync()).Players.Count);
        Assert.Equal(RoomError.RoomNotFound, missing.Error);
        Assert.True((await context.Manager.JoinAsync(room.Code, joiner, "Ben")).Success);
        Assert.Equal(2, (await room.GetSnapshotAsync()).Players.Count);
    }

    [Fact]
    public async Task HumansPlusNpcsAreLimitedToFour()
    {
        var context = new RoomTestContext();
        var host = Guid.NewGuid();
        var room = (await context.Manager.CreateRoomAsync(host, "Ana")).Value!;
        Assert.True((await context.Manager.JoinAsync(room.Code, Guid.NewGuid(), "Ben")).Success);
        Assert.True((await context.Manager.AddNpcAsync(room.Code, host)).Success);
        Assert.True((await context.Manager.AddNpcAsync(room.Code, host)).Success);

        Assert.Equal(RoomError.RoomFull, (await context.Manager.AddNpcAsync(room.Code, host)).Error);
        Assert.Equal(RoomError.RoomFull, (await context.Manager.JoinAsync(room.Code, Guid.NewGuid(), "Cvjetko")).Error);

        var npcId = (await room.GetSnapshotAsync()).Players.First(player => player.IsNpc).Id;
        Assert.True((await context.Manager.RemoveNpcAsync(room.Code, host, npcId)).Success);
        Assert.True((await context.Manager.JoinAsync(room.Code, Guid.NewGuid(), "Cvjetko")).Success);
    }

    [Fact]
    public async Task OnlyHostCanAddNpcRemoveNpcAndStart()
    {
        var context = new RoomTestContext();
        var host = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var room = (await context.Manager.CreateRoomAsync(host, "Ana")).Value!;
        await context.Manager.JoinAsync(room.Code, guest, "Ben");
        await context.Manager.AddNpcAsync(room.Code, host);
        var npcId = (await room.GetSnapshotAsync()).Players.Single(player => player.IsNpc).Id;

        Assert.Equal(RoomError.NotHost, (await context.Manager.AddNpcAsync(room.Code, guest)).Error);
        Assert.Equal(RoomError.NotHost, (await context.Manager.RemoveNpcAsync(room.Code, guest, npcId)).Error);
        Assert.Equal(RoomError.NotHost, (await context.Manager.StartGameAsync(room.Code, guest)).Error);
        Assert.Equal(RoomError.PlayerNotInRoom, (await context.Manager.StartGameAsync(room.Code, Guid.NewGuid())).Error);
        Assert.Equal(RoomState.Lobby, room.State);

        Assert.True((await context.Manager.StartGameAsync(room.Code, host)).Success);
        Assert.Equal(RoomState.InProgress, room.State);
    }

    [Fact]
    public async Task GameNeedsAtLeastTwoPlayersButOneHumanAndOneNpcIsEnough()
    {
        var context = new RoomTestContext();
        var host = Guid.NewGuid();
        var room = (await context.Manager.CreateRoomAsync(host, "Ana")).Value!;

        Assert.Equal(RoomError.NotEnoughPlayers, (await context.Manager.StartGameAsync(room.Code, host)).Error);
        await context.Manager.AddNpcAsync(room.Code, host);
        Assert.True((await context.Manager.StartGameAsync(room.Code, host)).Success);
        Assert.Equal(RoomError.NotInLobby, (await context.Manager.JoinAsync(room.Code, Guid.NewGuid(), "Kasno")).Error);
    }

    [Fact]
    public async Task InactiveRoomsAreRemovedAfterConfiguredTimeout()
    {
        var context = new RoomTestContext();
        var active = (await context.Manager.CreateRoomAsync(Guid.NewGuid(), "Ana")).Value!;
        var idle = (await context.Manager.CreateRoomAsync(Guid.NewGuid(), "Ben")).Value!;

        context.Time.Advance(TimeSpan.FromHours(1.5));
        await context.Manager.JoinAsync(active.Code, Guid.NewGuid(), "Cili");
        context.Time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(1, await context.Manager.RemoveInactiveAsync());
        Assert.Null(context.Manager.GetRoom(idle.Code));
        Assert.Null(context.Store.Get(idle.Code));
        Assert.NotNull(context.Manager.GetRoom(active.Code));
    }

    [Fact]
    public async Task LastHumanLeavingClosesRoomAndHostMovesOn()
    {
        var context = new RoomTestContext();
        var host = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var room = (await context.Manager.CreateRoomAsync(host, "Ana")).Value!;
        await context.Manager.JoinAsync(room.Code, guest, "Ben");

        Assert.True((await context.Manager.LeaveAsync(room.Code, host)).Success);
        Assert.Equal(guest, (await room.GetSnapshotAsync()).HostPlayerId);

        Assert.True((await context.Manager.LeaveAsync(room.Code, guest)).Success);
        Assert.Null(context.Manager.GetRoom(room.Code));
    }

    [Fact]
    public async Task SubscribersGetEventsUntilTheyDispose()
    {
        var context = new RoomTestContext();
        var host = Guid.NewGuid();
        var room = (await context.Manager.CreateRoomAsync(host, "Ana")).Value!;
        var received = new List<RoomEventType>();
        var subscription = room.Subscribe(roomEvent => received.Add(roomEvent.Type));

        await context.Manager.JoinAsync(room.Code, Guid.NewGuid(), "Ben");
        subscription.Dispose();
        subscription.Dispose();
        await context.Manager.AddNpcAsync(room.Code, host);

        Assert.Equal([RoomEventType.PlayerJoined], received);
    }

    [Fact]
    public async Task FaultySubscriberDoesNotBreakRoom()
    {
        var context = new RoomTestContext();
        var room = (await context.Manager.CreateRoomAsync(Guid.NewGuid(), "Ana")).Value!;
        using var _ = room.Subscribe(_ => throw new InvalidOperationException("boom"));

        Assert.True((await context.Manager.JoinAsync(room.Code, Guid.NewGuid(), "Ben")).Success);
    }
}
