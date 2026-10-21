using Sraz.UI.Blazor.Services;
using Xunit;

namespace Sraz.Tests;

public sealed class PlayerConnectionTrackerTests
{
    [Fact]
    public async Task PlayerDisconnectsOnlyWhenLastCircuitGoesDown()
    {
        var context = new RoomTestContext();
        var (room, playerId, _) = await context.StartTwoPlayerGameAsync();
        using var tracker = new PlayerConnectionTracker(context.Manager, context.Time);

        await tracker.SetConnectedAsync(playerId, "tab-one", true);
        await tracker.SetConnectedAsync(playerId, "tab-two", true);
        await tracker.SetConnectedAsync(playerId, "tab-one", false);

        Assert.False((await room.GetSnapshotAsync()).Players[0].IsDisconnected);

        await tracker.SetConnectedAsync(playerId, "tab-two", false);

        Assert.True((await room.GetSnapshotAsync()).Players[0].IsDisconnected);
        await tracker.SetConnectedAsync(playerId, "tab-two", false);
        await tracker.SetConnectedAsync(playerId, "tab-one", true);

        Assert.False((await room.GetSnapshotAsync()).Players[0].IsDisconnected);
        room.Close();
    }

    [Fact]
    public async Task RepeatedConnectionUpDoesNotCountCircuitTwice()
    {
        var context = new RoomTestContext();
        var (room, playerId, _) = await context.StartTwoPlayerGameAsync();
        using var tracker = new PlayerConnectionTracker(context.Manager, context.Time);

        await tracker.SetConnectedAsync(playerId, "tab", true);
        await tracker.SetConnectedAsync(playerId, "tab", true);
        await tracker.SetConnectedAsync(playerId, "tab", false);

        Assert.True((await room.GetSnapshotAsync()).Players[0].IsDisconnected);
        room.Close();
    }

    [Fact]
    public async Task DisconnectDoesNotAffectOtherPlayers()
    {
        var context = new RoomTestContext();
        var (room, firstId, secondId) = await context.StartTwoPlayerGameAsync();
        using var tracker = new PlayerConnectionTracker(context.Manager, context.Time);

        await tracker.SetConnectedAsync(firstId, "first", true);
        await tracker.SetConnectedAsync(secondId, "second", true);
        await tracker.SetConnectedAsync(firstId, "first", false);

        var players = (await room.GetSnapshotAsync()).Players;
        Assert.True(players[0].IsDisconnected);
        Assert.False(players[1].IsDisconnected);
        room.Close();
    }

    [Fact]
    public async Task FailedPresenceWriteIsRetriedWithoutAnotherCircuitTransition()
    {
        var context = new RoomTestContext();
        var (room, playerId, _) = await context.StartTwoPlayerGameAsync();
        using var tracker = new PlayerConnectionTracker(context.Manager, context.Time);
        await tracker.StartAsync(CancellationToken.None);
        await tracker.SetConnectedAsync(playerId, "tab", true);
        context.Store.FailWrites = true;
        await tracker.SetConnectedAsync(playerId, "tab", false);
        Assert.False((await room.GetSnapshotAsync()).Players[0].IsDisconnected);

        context.Store.FailWrites = false;
        context.Time.Advance(TimeSpan.FromSeconds(1));
        for (var attempt = 0; attempt < 100 && !(await room.GetSnapshotAsync()).Players[0].IsDisconnected; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True((await room.GetSnapshotAsync()).Players[0].IsDisconnected);
        await tracker.StopAsync(CancellationToken.None);
        room.Close();
    }
}
