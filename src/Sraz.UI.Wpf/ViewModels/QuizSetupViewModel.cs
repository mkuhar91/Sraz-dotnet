using Sraz.Domain.Models;
using Sraz.UI.Wpf.Localization;

namespace Sraz.UI.Wpf.ViewModels;

public sealed class QuizSetupViewModel : BindableBase
{
    private static readonly string[] CategoryValues =
    {
        "All", "General", "Science", "History", "Math", "Geography", "Literature", "Technology", "Art", "Sports", "Music"
    };

    private static readonly string[] DifficultyValues = { "Easy", "Medium", "Hard" };

    private static readonly string[] ModeValues =
    {
        nameof(GameMode.Classic), nameof(GameMode.Survival), nameof(GameMode.TimeAttack), nameof(GameMode.Daily)
    };

    private string _playerName = "Player";
    private string _selectedCategory = "All";
    private string _selectedDifficulty = "Easy";
    private string _selectedMode = nameof(GameMode.Classic);
    private string _selectedLanguage = Localizer.Instance.Language;
    private int _boardPlayerCount = 2;
    private string _boardPlayerOneName = "Player";
    private string _boardPlayerTwoName = "Player 2";
    private string _boardPlayerThreeName = "Player 3";
    private string _boardPlayerFourName = "Player 4";
    private string _boardSize = "10";

    public QuizSetupViewModel()
    {
        Categories = CategoryValues.Select(v => new ChoiceItem(v, Localizer.Instance.CategoryLabel(v))).ToList();
        Difficulties = DifficultyValues.Select(v => new ChoiceItem(v, Localizer.Instance.DifficultyLabel(v))).ToList();
        Modes = ModeValues.Select(v => new ChoiceItem(v, Localizer.Instance.ModeLabel(v))).ToList();
        Languages = new List<ChoiceItem>
        {
            new(Localizer.English, "English"),
            new(Localizer.Croatian, "Hrvatski")
        };
        BoardPlayerCounts = new[] { 1, 2, 3, 4 };

        Localizer.Instance.LanguageChanged += OnLanguageChanged;
    }

    public List<ChoiceItem> Categories { get; }

    public List<ChoiceItem> Difficulties { get; }

    public List<ChoiceItem> Modes { get; }

    public List<ChoiceItem> Languages { get; }
    public IReadOnlyList<int> BoardPlayerCounts { get; }

    public int BoardPlayerCount
    {
        get => _boardPlayerCount;
        set => SetProperty(ref _boardPlayerCount, value);
    }

    public string BoardPlayerOneName
    {
        get => _boardPlayerOneName;
        set => SetProperty(ref _boardPlayerOneName, value);
    }

    public string BoardPlayerTwoName
    {
        get => _boardPlayerTwoName;
        set => SetProperty(ref _boardPlayerTwoName, value);
    }

    public string BoardPlayerThreeName
    {
        get => _boardPlayerThreeName;
        set => SetProperty(ref _boardPlayerThreeName, value);
    }

    public string BoardPlayerFourName
    {
        get => _boardPlayerFourName;
        set => SetProperty(ref _boardPlayerFourName, value);
    }

    public string BoardSize
    {
        get => _boardSize;
        set => SetProperty(ref _boardSize, value);
    }

    public string PlayerName
    {
        get => _playerName;
        set => SetProperty(ref _playerName, value);
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _selectedCategory, value);
            }
        }
    }

    public string SelectedDifficulty
    {
        get => _selectedDifficulty;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _selectedDifficulty, value);
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
                RaisePropertyChanged(nameof(Mode));
                RaisePropertyChanged(nameof(ModeDescription));
                RaisePropertyChanged(nameof(FiltersEnabled));
            }
        }
    }

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || !SetProperty(ref _selectedLanguage, value))
            {
                return;
            }

            Localizer.Instance.SetLanguage(value);
            new AppSettings { Language = Localizer.Instance.Language }.Save();
        }
    }

    public GameMode Mode => Enum.TryParse<GameMode>(_selectedMode, out var mode) ? mode : GameMode.Classic;

    public string ModeDescription => Localizer.Instance.Get("ModeHint_" + _selectedMode);

    public bool FiltersEnabled => Mode != GameMode.Daily;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in Categories)
        {
            item.Label = Localizer.Instance.CategoryLabel(item.Value);
        }

        foreach (var item in Difficulties)
        {
            item.Label = Localizer.Instance.DifficultyLabel(item.Value);
        }

        foreach (var item in Modes)
        {
            item.Label = Localizer.Instance.ModeLabel(item.Value);
        }

        RaisePropertyChanged(nameof(ModeDescription));
    }
}
