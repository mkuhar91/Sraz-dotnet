using System.Collections;
using System.Globalization;
using System.Resources;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Sraz.Application.Abstractions;
using Sraz.Application.Rooms;
using Sraz.Domain.Models;
using Sraz.UI.Blazor.Components;
using Sraz.UI.Blazor.Localization;
using Sraz.UI.Blazor.Pages;
using Xunit;

namespace Sraz.Tests;

public sealed class BlazorLocalizationTests : TestContext
{
    public BlazorLocalizationTests()
    {
        Services.AddLocalization();
        Services.AddScoped<Texts>();
        Services.AddSingleton(TimeProvider.System);
    }

    [Theory]
    [InlineData("hr", "Čekaonica", "Geografija", "Nisi na redu.")]
    [InlineData("en", "Lobby", "Geography", "It is not your turn.")]
    public void LocalizerResolvesLabelsThemesErrorsAndFormatting(string culture, string lobby, string theme, string error)
    {
        using var scope = new CultureScope(culture);
        var texts = Services.GetRequiredService<Texts>();
        Assert.Equal(lobby, texts.Lobby);
        Assert.Equal(theme, texts.Theme(QuestionTheme.Geography));
        Assert.Equal(error, texts.Error(RoomError.NotYourTurn));
        Assert.Contains("Ana", texts.PlayerDisconnected("Ana"));
        Assert.Contains("Ana", texts.NpcTakeover("Ana"));
        Assert.Contains("Ana", texts.PlayerReturned("Ana"));
        Assert.Equal("2/3", texts.QuestionOf(2, 3));
        Assert.Equal(string.Empty, texts.Notice("unknown"));
    }

    [Fact]
    public void EnglishAndCroatianHaveIdenticalResourceKeysAndFormatArguments()
    {
        var manager = new ResourceManager(typeof(Texts));
        var hr = manager.GetResourceSet(CultureInfo.GetCultureInfo("hr"), true, true)!;
        var en = manager.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, true)!;
        var hrValues = hr.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        var enValues = en.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        Assert.Equal(hrValues.Keys.Order(), enValues.Keys.Order());
        foreach (var key in hrValues.Keys)
        {
            var placeholders = (string text) => System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}")
                .Select(m => m.Value).Order().ToArray();
            Assert.Equal(placeholders(hrValues[key]), placeholders(enValues[key]));
        }
        foreach (var culture in new[] { "hr", "en" })
        {
            using var scope = new CultureScope(culture);
            var localizer = Services.GetRequiredService<IStringLocalizer<Texts>>();
            foreach (var theme in Enum.GetValues<QuestionTheme>())
                Assert.False(localizer[$"Theme_{theme}"].ResourceNotFound);
            foreach (var error in Enum.GetValues<RoomError>().Where(e => e != RoomError.None))
                Assert.False(string.IsNullOrWhiteSpace(Services.GetRequiredService<Texts>().Error(error)));
        }
    }

    [Fact]
    public void HeaderUsesCultureEndpointWithLocalReturnPath()
    {
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/game/ABCDE?test=1");
        var cut = RenderComponent<SiteHeader>();
        var languages = cut.FindAll("a[hreflang]");
        Assert.Equal(2, languages.Count);
        Assert.Contains("culture=hr", languages[0].GetAttribute("href"));
        Assert.Contains("returnUrl=%2Fgame%2FABCDE%3Ftest%3D1", languages[0].GetAttribute("href"));
        Assert.Contains("culture=en", languages[1].GetAttribute("href"));
        languages[1].Click();
        var navigation = Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>();
        Assert.True(navigation.History.First().Options.ForceLoad);
        Assert.Contains("/culture?culture=en", navigation.Uri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DialogRejectsDuplicateAnswerAndUnlocksOnlyForNextQuestion(bool duel)
    {
        var question = new QuestionDto(Guid.NewGuid(), "Geography", "Question", null, ["A", "B"], 1, duel ? 3 : 1, duel, DateTimeOffset.UtcNow.AddMinutes(1));
        var answers = 0;
        if (duel)
        {
            var cut = RenderComponent<DuelDialog>(p => p.Add(d => d.Question, question).Add(d => d.OnAnswer, _ => answers++));
            cut.FindAll("button.option")[0].Click();
            cut.FindAll("button.option")[1].Click();
            Assert.Equal(1, answers);
            cut.SetParametersAndRender(p => p.Add(d => d.Question, question with { Number = 2 }));
            cut.FindAll("button.option")[1].Click();
            Assert.Equal(2, answers);
        }
        else
        {
            var cut = RenderComponent<QuestionDialog>(p => p.Add(d => d.Question, question).Add(d => d.OnAnswer, _ => answers++));
            cut.FindAll("button.option")[0].Click();
            cut.FindAll("button.option")[1].Click();
            Assert.Equal(1, answers);
            cut.SetParametersAndRender(p => p.Add(d => d.Question, question with { QuestionId = Guid.NewGuid() }));
            cut.FindAll("button.option")[1].Click();
            Assert.Equal(2, answers);
        }
    }

    [Fact]
    public void GameOverHandlesMissingWinnerAndOnlyOffersRestartToHost()
    {
        using var culture = new CultureScope("en");
        var view = new BoardViewDto("ABCDE", RoomState.Finished, 6, [],
            [new PlayerViewDto(0, Guid.NewGuid(), "Ana", "#fff", false, true, 2)],
            [], 0, 99, 0, false, []);
        var cut = RenderComponent<GameOver>(p => p.Add(c => c.View, view));
        Assert.Contains("No winner.", cut.Markup);
        Assert.Contains("Ana", cut.Find(".game-summary").TextContent);
        Assert.Single(cut.FindAll("button"));
        Assert.Single(cut.FindAll("a[href='/leaderboard']"));
        cut.SetParametersAndRender(p => p.Add(c => c.View, view with { ViewerIndex = -1 }));
        Assert.Empty(cut.FindAll("button"));
        Assert.Contains("Waiting for the host", cut.Markup);
    }

    [Fact]
    public void LeaderboardRendersRowsInRepositoryOrder()
    {
        using var culture = new CultureScope("en");
        Services.AddSingleton<IRoomLeaderboard>(new StubLeaderboard(false,
            [new RoomLeaderboardEntry(Guid.NewGuid(), "Ana", 3, 5, DateTimeOffset.UtcNow)]));
        var cut = RenderComponent<Leaderboard>();
        Assert.Contains("Leaderboard", cut.Find("h1").TextContent);
        Assert.Contains("Ana", cut.Find("tbody").TextContent);
        Assert.Equal("3", cut.FindAll("tbody td")[1].TextContent);
        Assert.Contains("Win percentage", cut.Find("thead").TextContent);
        Assert.Equal("60%", cut.FindAll("tbody td")[3].TextContent);
    }

    [Fact]
    public void LeaderboardShowsZeroWinPercentageWithoutDivisionByZero()
    {
        using var culture = new CultureScope("en");
        Services.AddSingleton<IRoomLeaderboard>(new StubLeaderboard(false,
            [new RoomLeaderboardEntry(Guid.NewGuid(), "Ana", 0, 0, DateTimeOffset.UtcNow)]));
        var cut = RenderComponent<Leaderboard>();
        Assert.Equal("0%", cut.FindAll("tbody td")[3].TextContent);
    }

    [Theory]
    [InlineData(false, "No games have been played yet.")]
    [InlineData(true, "The leaderboard is currently unavailable.")]
    public void LeaderboardLocalizesEmptyAndFailureStates(bool fails, string expected)
    {
        using var culture = new CultureScope("en");
        Services.AddSingleton<IRoomLeaderboard>(new StubLeaderboard(fails, []));
        var cut = RenderComponent<Leaderboard>();
        Assert.Contains(expected, cut.Markup);
        Assert.DoesNotContain("private database details", cut.Markup);
    }

    private sealed class StubLeaderboard(bool fails, IReadOnlyList<RoomLeaderboardEntry> rows) : IRoomLeaderboard
    {
        public Task<IReadOnlyList<RoomLeaderboardEntry>> GetTopAsync(int count = 100, CancellationToken cancellationToken = default) =>
            fails ? Task.FromException<IReadOnlyList<RoomLeaderboardEntry>>(new Exception("private database details")) : Task.FromResult(rows);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
