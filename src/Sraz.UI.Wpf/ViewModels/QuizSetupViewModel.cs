namespace Sraz.UI.Wpf.ViewModels;

public sealed class QuizSetupViewModel : BindableBase
{
    private string _playerName = "Player";
    private string _selectedCategory = "All";
    private string _selectedDifficulty = "Easy";

    public string PlayerName
    {
        get => _playerName;
        set => SetProperty(ref _playerName, value);
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set => SetProperty(ref _selectedCategory, value);
    }

    public string SelectedDifficulty
    {
        get => _selectedDifficulty;
        set => SetProperty(ref _selectedDifficulty, value);
    }

    public List<string> Categories { get; } = new()
    {
        "All",
        "General",
        "Science",
        "History",
        "Math",
        "Geography",
        "Literature",
        "Technology",
        "Art",
        "Sports",
        "Music"
    };

    public List<string> Difficulties { get; } = new() { "Easy", "Medium", "Hard" };
}
