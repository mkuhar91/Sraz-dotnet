using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Sraz.Application.Rooms;
using Sraz.Domain.Models;
using Sraz.UI.Blazor.Pages;
using Sraz.UI.Blazor.Services;
using Sraz.UI.Blazor.Localization;
using Xunit;

namespace Sraz.Tests;

public sealed class BlazorPageTests : TestContext
{
    private sealed class FixedIdentity(Guid id) : IPlayerIdentity
    {
        public Guid GetOrCreatePlayerId() => id;
    }

    private readonly RoomTestContext _rooms = new();

    private void Register(Guid playerId, IRoomManager? manager = null)
    {
        Services.AddLocalization();
        Services.AddScoped<Texts>();
        Services.AddSingleton<IRoomManager>(manager ?? _rooms.Manager);
        Services.AddSingleton<IPlayerIdentity>(new FixedIdentity(playerId));
        Services.AddSingleton(new NicknameStore());
        Services.AddSingleton<TimeProvider>(_rooms.Time);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task LobbyDisablesAddNpcWhenRoomIsFullAndUpdatesLive()
    {
        var host = Guid.NewGuid();
        Register(host);
        var room = (await _rooms.Manager.CreateRoomAsync(host, "Ana", 8, seed: 1)).Value!;
        var cut = RenderComponent<Lobby>(p => p.Add(l => l.Code, room.Code));
        Assert.Contains(room.Code, cut.Find("[data-testid=room-code]").TextContent);

        await room.AddNpcAsync(host);
        await room.JoinAsync(Guid.NewGuid(), "Ben");

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".player-list li").Count));
        await room.AddNpcAsync(host);
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("button").First(b => b.TextContent.Contains("Dodaj NPC")).HasAttribute("disabled")));
    }

    [Fact]
    public async Task LobbyStartIsHostOnlyAndRequiresTwoPlayers()
    {
        var host = Guid.NewGuid();
        Register(host);
        var room = (await _rooms.Manager.CreateRoomAsync(host, "Ana", 8, seed: 1)).Value!;
        var cut = RenderComponent<Lobby>(p => p.Add(l => l.Code, room.Code));
        var start = () => cut.FindAll("button").First(b => b.TextContent.Contains("Pokreni igru"));

        Assert.True(start().HasAttribute("disabled"));
        cut.FindAll("button").First(b => b.TextContent.Contains("Dodaj NPC")).Click();

        cut.WaitForAssertion(() => Assert.False(start().HasAttribute("disabled")));
    }

    [Fact]
    public async Task GameRedirectsNonMembersToStartPage()
    {
        Register(Guid.NewGuid());
        var (room, _, _) = await _rooms.StartTwoPlayerGameAsync();

        RenderComponent<Game>(p => p.Add(g => g.Code, room.Code));

        Assert.Contains("notmember", Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    [Fact]
    public void GameRedirectsFromUnknownRoomToStartPageWithMessage()
    {
        Register(Guid.NewGuid());

        RenderComponent<Game>(p => p.Add(g => g.Code, "NOPE1"));

        Assert.Contains("notice=notfound", Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    [Fact]
    public async Task GamePlayerCanSelectPawnMoveAndAnswer()
    {
        var (room, p0, _) = await _rooms.StartTwoPlayerGameAsync();
        Register(p0);
        var cut = RenderComponent<Game>(p => p.Add(g => g.Code, room.Code));
        var firstPawn = cut.FindAll("button.pawn-button").First();

        firstPawn.Click();
        var target = cut.FindAll(".cell.target").First();
        Assert.NotEmpty(cut.FindAll(".cell.target"));
        target.Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".modal")));
        cut.FindAll("button.option")[TestQuestionProvider.CorrectIndex].Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".modal")));
        var after = await room.GetViewAsync(p0);
        Assert.True(after.Pawns.Any(pawn => pawn.HasActedThisTurn) || after.CurrentPlayerIndex == 1);
    }

    [Fact]
    public async Task OtherPlayersSeeWhoIsAnsweringButNoDialog()
    {
        var (room, p0, p1) = await _rooms.StartTwoPlayerGameAsync();
        Register(p1);
        var cut = RenderComponent<Game>(p => p.Add(g => g.Code, room.Code));

        await RoomTestContext.BeginAnyMoveAsync(room, p0, 0);

        cut.WaitForAssertion(() => Assert.Contains("Ana odgovara na pitanje", cut.Find(".status-line").TextContent));
        Assert.Empty(cut.FindAll(".modal"));
    }

    [Fact]
    public async Task FailedAnswerPersistenceAllowsRetryOnSameQuestion()
    {
        var (room, host, _) = await _rooms.StartTwoPlayerGameAsync();
        Register(host);
        var cut = RenderComponent<Game>(p => p.Add(g => g.Code, room.Code));
        cut.FindAll("button.pawn-button").First().Click();
        cut.FindAll(".cell.target").First().Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.option")));
        _rooms.Store.FailWrites = true;

        cut.FindAll("button.option")[TestQuestionProvider.CorrectIndex].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll(".toast.error"));
            Assert.All(cut.FindAll("button.option"), option => Assert.False(option.HasAttribute("disabled")));
        });
        Assert.True((await room.GetViewAsync(host)).HasPendingQuestion);
        _rooms.Store.FailWrites = false;
        cut.FindAll("button.option")[TestQuestionProvider.CorrectIndex].Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".modal")));
        Assert.False((await room.GetViewAsync(host)).HasPendingQuestion);
    }

    [Fact]
    public async Task GameUnsubscribesFromRoomWhenDisposed()
    {
        var (room, p0, _) = await _rooms.StartTwoPlayerGameAsync();
        Register(p0);
        var cut = RenderComponent<Game>(p => p.Add(g => g.Code, room.Code));
        var renders = cut.RenderCount;

        DisposeComponents();
        await RoomTestContext.BeginAnyMoveAsync(room, p0, 0);

        Assert.Equal(renders, cut.RenderCount);
    }

    [Fact]
    public async Task GameShowsDisconnectReturnAndTakeoverNotificationsWithoutLosingMembership()
    {
        var (room, host, guest) = await _rooms.StartTwoPlayerGameAsync();
        Register(guest);
        var cut = RenderComponent<Game>(p => p.Add(g => g.Code, room.Code));

        await room.SetConnectedAsync(guest, false);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("odspojen", cut.Find(".players").TextContent);
            Assert.Contains("Ben se odspojio.", cut.Find(".notices").TextContent);
        });
        _rooms.Time.Advance(TimeSpan.FromSeconds(60));
        await room.WaitForTimeoutHandlingAsync();
        cut.WaitForAssertion(() => Assert.Contains("NPC je preuzeo pijune igrača Ben.", cut.Find(".notices").TextContent));
        Assert.DoesNotContain("notmember", Services.GetRequiredService<FakeNavigationManager>().Uri);

        await room.SetConnectedAsync(guest, true);
        for (var i = 0; i < 3 && (await room.GetViewAsync(host)).CurrentPlayerIndex == 0; i++)
        {
            await RoomTestContext.BeginAnyMoveAsync(room, host, 0);
            await room.AnswerAsync(host, TestQuestionProvider.CorrectIndex);
        }
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("odspojen", cut.Find(".players").TextContent);
            Assert.Contains("Ben se vratio.", cut.Find(".notices").TextContent);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FinishedGameRestartsForHostAndGuestNavigatesViaRoomEvent(bool viewerIsHost)
    {
        var (room, manager, host, guest, attacker, _) = await _rooms.ArrangeDuelAsync(lastDefender: true);
        await room.BeginMoveAsync(host, attacker, Direction.Right);
        for (var i = 0; i < 3; i++)
            await room.AnswerAsync(host, TestQuestionProvider.CorrectIndex);
        Assert.Equal(RoomState.Finished, room.State);
        Register(viewerIsHost ? host : guest, manager);
        var cut = RenderComponent<Game>(p => p.Add(g => g.Code, room.Code));
        Assert.Contains("Ana", cut.Find(".game-over .winner").TextContent);
        if (viewerIsHost)
            cut.Find(".game-over button").Click();
        else
        {
            Assert.Empty(cut.FindAll(".game-over button"));
            await room.RestartAsync(host);
        }

        cut.WaitForAssertion(() => Assert.Contains($"/lobby/{room.Code}", Services.GetRequiredService<FakeNavigationManager>().Uri));
        Assert.Equal(RoomState.Lobby, room.State);
    }

    [Fact]
    public async Task LobbyUnsubscribesFromPreviousRoomWhenRouteCodeChanges()
    {
        var host = Guid.NewGuid();
        Register(host);
        var first = (await _rooms.Manager.CreateRoomAsync(host, "Ana")).Value!;
        var second = (await _rooms.Manager.CreateRoomAsync(host, "Ana")).Value!;
        var cut = RenderComponent<Lobby>(p => p.Add(l => l.Code, first.Code));
        cut.SetParametersAndRender(p => p.Add(l => l.Code, second.Code));
        var renders = cut.RenderCount;

        await first.AddNpcAsync(host);
        Assert.Equal(renders, cut.RenderCount);
        Assert.Equal(second.Code, cut.Find("[data-testid=room-code]").TextContent);
    }
}
