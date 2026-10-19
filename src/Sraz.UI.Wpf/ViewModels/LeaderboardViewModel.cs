using Sraz.Application.Services;
using Sraz.Domain.Models;
using Sraz.UI.Wpf.Localization;

namespace Sraz.UI.Wpf.ViewModels;

public sealed class LeaderboardRow
{
    public string PlayerName { get; init; } = string.Empty;
    public int Score { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Difficulty { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public int CorrectAnswers { get; init; }
    public int WrongAnswers { get; init; }
    public int Timeouts { get; init; }
    public int BestStreak { get; init; }
    public string PlayedAt { get; init; } = string.Empty;
}

public sealed class LeaderboardViewModel : BindableBase
{
    private static readonly string[] PeriodValues = { "AllTime", "Today", "Week", "Month" };

    private readonly LeaderboardService _service;
    private string _selectedCategory = "All";
    private string _selectedDifficulty = "All";
    private string _selectedMode = "All";
    private string _selectedPeriod = "AllTime";

    public LeaderboardViewModel(LeaderboardService service)
    {
        _service = service;
        var loc = Localizer.Instance;

        Categories = new[] { "All", "General", "Science", "History", "Math", "Geography", "Literature", "Technology", "Art", "Sports", "Music" }
            .Select(v => new ChoiceItem(v, loc.CategoryLabel(v))).ToList();
        Difficulties = new[] { "All", "Easy", "Medium", "Hard" }
            .Select(v => new ChoiceItem(v, loc.DifficultyLabel(v))).ToList();
        Modes = new[] { "All", nameof(GameMode.Classic), nameof(GameMode.Survival), nameof(GameMode.TimeAttack), nameof(GameMode.Daily) }
            .Select(v => new ChoiceItem(v, loc.ModeLabel(v))).ToList();
        Periods = PeriodValues.Select(v => new ChoiceItem(v, loc.Get("Period_" + v))).ToList();

        Entries = new List<LeaderboardRow>();
        Reload();
    }

    public List<ChoiceItem> Categories { get; }

    public List<ChoiceItem> Difficulties { get; }

    public List<ChoiceItem> Modes { get; }

    public List<ChoiceItem> Periods { get; }

    public List<LeaderboardRow> Entries { get; private set; }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value is not null && SetProperty(ref _selectedCategory, value))
            {
                Reload();
            }
        }
    }

    public string SelectedDifficulty
    {
        get => _selectedDifficulty;
        set
        {
            if (value is not null && SetProperty(ref _selectedDifficulty, value))
            {
                Reload();
            }
        }
    }

    public string SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (value is not null && SetProperty(ref _selectedMode, value))
            {
                Reload();
            }
        }
    }

    public string SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (value is not null && SetProperty(ref _selectedPeriod, value))
            {
                Reload();
            }
        }
    }

    public void Reload()
    {
        var loc = Localizer.Instance;
        var filter = new LeaderboardFilter(_selectedCategory, _selectedDifficulty, _selectedMode, GetSince(_selectedPeriod));

        Entries = _service.GetTopEntries(10, filter)
            .Select(e => new LeaderboardRow
            {
                PlayerName = e.PlayerName,
                Score = e.Score,
                Category = loc.CategoryLabel(e.Category),
                Difficulty = loc.DifficultyLabel(e.Difficulty),
                Mode = loc.ModeLabel(e.Mode),
                CorrectAnswers = e.CorrectAnswers,
                WrongAnswers = e.WrongAnswers,
                Timeouts = e.Timeouts,
                BestStreak = e.BestStreak,
                PlayedAt = e.PlayedAtUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)
            })
            .ToList();

        RaisePropertyChanged(nameof(Entries));
    }

    public void SaveCurrentGame(string playerName, QuizRound round, string category, string difficulty)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            playerName = "Player";
        }

        var isDaily = round.Mode == GameMode.Daily;

        var entry = new LeaderboardEntry
        {
            PlayerName = playerName,
            Score = round.Score,
            CorrectAnswers = round.CorrectAnswers,
            WrongAnswers = round.WrongAnswers,
            Timeouts = round.Timeouts,
            BestStreak = round.BestStreak,
            Category = isDaily ? "All" : category,
            Difficulty = isDaily ? "All" : difficulty,
            Mode = round.Mode.ToString(),
            PlayedAtUtc = DateTime.UtcNow
        };

        _service.SaveEntry(entry);
        Reload();
    }

    private static DateTime? GetSince(string period)
    {
        return period switch
        {
            "Today" => DateTime.Now.Date.ToUniversalTime(),
            "Week" => DateTime.UtcNow.AddDays(-7),
            "Month" => DateTime.UtcNow.AddDays(-30),
            _ => null
        };
    }
}
