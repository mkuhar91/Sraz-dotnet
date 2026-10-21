using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Sraz.Application.Rooms;
using Sraz.Domain.Models;
using Sraz.UI.Blazor.Components;
using Sraz.UI.Blazor.Localization;
using Xunit;
using BoardComponent = Sraz.UI.Blazor.Components.Board;

namespace Sraz.Tests;

public sealed class BlazorComponentTests : TestContext
{
    private static readonly DateTimeOffset Start = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Start);

    public BlazorComponentTests()
    {
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddLocalization();
        Services.AddScoped<Texts>();
    }

    private static readonly Guid Mine = Guid.NewGuid();
    private static readonly Guid Acted = Guid.NewGuid();
    private static readonly Guid Foreign = Guid.NewGuid();

    private static BoardViewDto BuildView(bool myTurn = true) => new(
        "ABCDE", RoomState.InProgress, 6,
        Enumerable.Range(0, 36).Select(i => (QuestionTheme)(i % 8)).ToList(),
        [
            new PlayerViewDto(0, Guid.NewGuid(), "Ana", "#C62828", false, true, 3),
            new PlayerViewDto(1, Guid.NewGuid(), "Ivo", "#1565C0", false, false, 1)
        ],
        [
            new PawnViewDto(Mine, 0, 2, 2, false, false),
            new PawnViewDto(Acted, 0, 0, 0, false, true),
            new PawnViewDto(Foreign, 1, 2, 3, false, false)
        ],
        myTurn ? 0 : 1, null, 0, false,
        myTurn
            ? [new MoveTargetDto(Mine, Direction.Up, 1, 2), new MoveTargetDto(Mine, Direction.Right, 2, 3)]
            : []);

    private static QuestionDto Question(int number = 1, int total = 1, bool duel = false) => new(
        Guid.NewGuid(), "Geography", "Glavni grad Hrvatske?", null, ["Split", "Zagreb", "Rijeka", "Osijek"],
        number, total, duel, Start.AddSeconds(30));

    [Fact]
    public void BoardRendersGridAndOnlyAllowedCellsGetBlackBorderAfterSelection()
    {
        var cut = RenderComponent<BoardComponent>(p => p.Add(b => b.View, BuildView()).Add(b => b.SelectedPawnId, Mine));

        Assert.Equal(36, cut.FindAll(".cell").Count);
        Assert.Contains("--n:6", cut.Find(".board").GetAttribute("style"));
        var targets = cut.FindAll(".cell.target");
        Assert.Equal(2, targets.Count);
        Assert.Equal(["1,2", "2,3"], targets.Select(t => $"{t.GetAttribute("data-row")},{t.GetAttribute("data-column")}").OrderBy(x => x).ToArray());
    }

    [Fact]
    public void BoardShowsNoTargetsUntilAPawnIsSelected()
    {
        var cut = RenderComponent<BoardComponent>(p => p.Add(b => b.View, BuildView()));

        Assert.Empty(cut.FindAll(".cell.target"));
    }

    [Fact]
    public void ClickingOwnPawnSelectsItAndClickingTargetRequestsTheMove()
    {
        Guid? selected = null;
        MoveTargetDto? move = null;
        var cut = RenderComponent<BoardComponent>(p => p
            .Add(b => b.View, BuildView())
            .Add(b => b.SelectedPawnId, Mine)
            .Add(b => b.OnPawnSelected, id => selected = id)
            .Add(b => b.OnMoveSelected, m => move = m));

        cut.Find(".cell[data-row='2'][data-column='2'] button.pawn-button").Click();
        cut.Find(".cell[data-row='2'][data-column='3']").Click();

        Assert.Equal(Mine, selected);
        Assert.Equal(new MoveTargetDto(Mine, Direction.Right, 2, 3), move);
    }

    [Fact]
    public void PawnThatAlreadyActedIsMarkedAndNotClickable()
    {
        var cut = RenderComponent<BoardComponent>(p => p.Add(b => b.View, BuildView()));

        var acted = cut.Find(".cell[data-row='0'][data-column='0'] .pawn");
        Assert.Contains("acted", acted.ClassList);
        Assert.Empty(cut.FindAll(".cell[data-row='0'][data-column='0'] button"));
        Assert.Empty(cut.FindAll(".cell[data-row='2'][data-column='3'] button"));
    }

    [Fact]
    public void NothingIsClickableWhenItIsNotTheViewersTurn()
    {
        var cut = RenderComponent<BoardComponent>(p => p.Add(b => b.View, BuildView(myTurn: false)));

        Assert.Empty(cut.FindAll("button.pawn-button"));
        Assert.Empty(cut.FindAll(".cell.target"));
    }

    [Fact]
    public void PawnsAreRenderedAsInlineSvgInThePlayerColour()
    {
        var cut = RenderComponent<BoardComponent>(p => p.Add(b => b.View, BuildView()));

        Assert.Contains("#C62828", cut.Find(".cell[data-row='2'][data-column='2'] svg circle").GetAttribute("fill"));
        Assert.Contains("#1565C0", cut.Find(".cell[data-row='2'][data-column='3'] svg circle").GetAttribute("fill"));
    }

    [Fact]
    public void LegendListsAllEightThemes()
    {
        var cut = RenderComponent<Legend>();

        Assert.Equal(8, cut.FindAll(".legend li").Count);
    }

    [Fact]
    public void QuestionDialogSendsOnlyTheClickedIndex()
    {
        int? answered = null;
        var cut = RenderComponent<QuestionDialog>(p => p
            .Add(d => d.Question, Question())
            .Add(d => d.OnAnswer, index => answered = index));

        cut.FindAll("button.option")[2].Click();

        Assert.Equal(2, answered);
    }

    [Fact]
    public void QuestionDialogCountsDownFromTheServerExpiryTime()
    {
        var cut = RenderComponent<QuestionDialog>(p => p.Add(d => d.Question, Question()));
        Assert.Contains("30", cut.Find(".countdown-text").TextContent);

        _time.Advance(TimeSpan.FromSeconds(10));

        cut.WaitForAssertion(() => Assert.Contains("20", cut.Find(".countdown-text").TextContent));
    }

    [Fact]
    public void QuestionDialogLocksAnswersAndRaisesExpiredWhenTimeRunsOut()
    {
        var expired = 0;
        int? answered = null;
        var cut = RenderComponent<QuestionDialog>(p => p
            .Add(d => d.Question, Question())
            .Add(d => d.OnAnswer, index => answered = index)
            .Add(d => d.OnExpired, () => expired++));

        _time.Advance(TimeSpan.FromSeconds(30));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, expired);
            Assert.All(cut.FindAll("button.option"), button => Assert.True(button.HasAttribute("disabled")));
            Assert.NotEmpty(cut.FindAll(".expired"));
        });
        Assert.Null(answered);
    }

    [Fact]
    public void QuestionDialogCanBeDisabledWhileAnAnswerIsSubmitted()
    {
        var cut = RenderComponent<QuestionDialog>(p => p.Add(d => d.Question, Question()).Add(d => d.Disabled, true));

        Assert.All(cut.FindAll("button.option"), button => Assert.True(button.HasAttribute("disabled")));
    }

    [Fact]
    public void DuelDialogShowsProgressAndSendsIndex()
    {
        int? answered = null;
        var cut = RenderComponent<DuelDialog>(p => p
            .Add(d => d.Question, Question(number: 2, total: 3, duel: true))
            .Add(d => d.OnAnswer, index => answered = index));

        Assert.Contains("2/3", cut.Find(".duel-count").TextContent);
        Assert.Equal(3, cut.FindAll(".duel-progress .step").Count);
        Assert.Single(cut.FindAll(".step.done"));
        Assert.Single(cut.FindAll(".step.current"));

        cut.FindAll("button.option")[1].Click();

        Assert.Equal(1, answered);
    }

    [Fact]
    public void DuelDialogStopsAtFirstWrongAnswerAndShowsOutcome()
    {
        var closed = false;
        var result = new AnswerResultDto(false, true, MoveOutcome.Stayed, null);
        var cut = RenderComponent<DuelDialog>(p => p
            .Add(d => d.Question, Question(number: 1, total: 3, duel: true))
            .Add(d => d.Result, result)
            .Add(d => d.OnClose, () => closed = true));

        Assert.Empty(cut.FindAll("button.option"));
        Assert.Single(cut.FindAll(".step.failed"));
        Assert.NotEmpty(cut.FindAll(".duel-result"));

        cut.Find(".duel-result button").Click();

        Assert.True(closed);
    }

    [Fact]
    public void DialogMarkupDoesNotRevealTheCorrectAnswer()
    {
        var cut = RenderComponent<QuestionDialog>(p => p.Add(d => d.Question, Question()));

        var classes = cut.FindAll("button.option").Select(button => button.ClassName).Distinct().ToList();
        Assert.Single(classes);
        Assert.DoesNotContain("correct", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explanation", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClientFacingDtosHaveNoCorrectAnswerMember()
    {
        foreach (var type in new[] { typeof(QuestionDto), typeof(BoardViewDto), typeof(PlayerViewDto), typeof(PawnViewDto), typeof(MoveTargetDto) })
        {
            Assert.DoesNotContain(type.GetProperties(), property =>
                property.Name.Contains("Correct", StringComparison.OrdinalIgnoreCase) && property.PropertyType != typeof(bool) ||
                property.Name.Contains("Explanation", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task RoomViewOffersMovesOnlyToThePlayerOnTurnAndLeaksNoAnswers()
    {
        var context = new RoomTestContext();
        var (room, p0, p1) = await context.StartTwoPlayerGameAsync();

        var first = await room.GetViewAsync(p0);
        var second = await room.GetViewAsync(p1);
        var (_, question) = await RoomTestContext.BeginAnyMoveAsync(room, p0, 0);
        var pending = await room.GetViewAsync(p0);

        Assert.NotEmpty(first.AllowedMoves);
        Assert.Empty(second.AllowedMoves);
        Assert.Equal(36, first.Themes.Count);
        Assert.True(pending.HasPendingQuestion);
        Assert.Empty(pending.AllowedMoves);
        Assert.False(first.Players[0].PawnsRemaining == 0);
        var json = System.Text.Json.JsonSerializer.Serialize(pending);
        Assert.DoesNotContain("Because B", json);
        Assert.NotNull(question);
    }

    [Fact]
    public async Task OnlyHostCanChangeBoardSizeInTheLobby()
    {
        var context = new RoomTestContext();
        var host = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var room = (await context.Manager.CreateRoomAsync(host, "Ana", 8, seed: 1)).Value!;
        await room.JoinAsync(guest, "Ivo");

        Assert.Equal(RoomError.NotHost, (await room.SetBoardSizeAsync(guest, 9)).Error);
        Assert.Equal(RoomError.InvalidBoardSize, (await room.SetBoardSizeAsync(host, 5)).Error);
        Assert.True((await room.SetBoardSizeAsync(host, 12)).Success);
        Assert.Equal(12, (await room.GetViewAsync(host)).BoardSize);
    }
}
