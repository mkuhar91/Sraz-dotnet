using System.IO;
using System.Windows;
using Sraz.Application.Services;
using Sraz.Infrastructure.Persistence;
using Sraz.UI.Wpf.ViewModels;

namespace Sraz.UI.Wpf;

public partial class MainWindow : Window
{
    private readonly QuizSetupViewModel _setupViewModel;

    public MainWindow()
    {
        InitializeComponent();
        _setupViewModel = new QuizSetupViewModel();
        DataContext = _setupViewModel;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        var playerName = _setupViewModel.PlayerName.Trim();
        if (string.IsNullOrWhiteSpace(playerName))
        {
            MessageBox.Show("Please enter a player name before starting.", "Sraz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dataPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "questions.seed.json"));
        var provider = new JsonQuestionProvider(dataPath);
        var roundService = new QuizRoundService(provider);
        var round = await roundService.CreateRoundAsync(10, _setupViewModel.SelectedCategory, _setupViewModel.SelectedDifficulty);

        if (round.Questions.Count == 0)
        {
            MessageBox.Show("No questions found for the selected category/difficulty. Please adjust the filters.", "Sraz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var vm = new QuestionViewModel(round);
        var window = new QuestionWindow(vm);
        window.Owner = this;
        window.ShowDialog();

        var summaryVm = new RoundSummaryViewModel(
            round.Score,
            round.Questions.Count,
            round.CorrectAnswers,
            round.WrongAnswers,
            round.Timeouts);

        var summary = new RoundSummaryWindow(summaryVm) { Owner = this };
        summary.ShowDialog();

        var leaderboardPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "leaderboard.json"));
        var leaderboardViewModel = new LeaderboardViewModel(
            new LeaderboardService(new LeaderboardStore(leaderboardPath)));

        leaderboardViewModel.SaveCurrentGame(
            playerName,
            round.Score,
            round.CorrectAnswers,
            round.WrongAnswers,
            round.Timeouts,
            _setupViewModel.SelectedCategory,
            _setupViewModel.SelectedDifficulty);
    }

    private void Leaderboard_Click(object sender, RoutedEventArgs e)
    {
        var leaderboardPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "leaderboard.json"));
        var viewModel = new LeaderboardViewModel(new LeaderboardService(new LeaderboardStore(leaderboardPath)));
        var leaderboardWindow = new LeaderboardWindow(viewModel) { Owner = this };
        leaderboardWindow.ShowDialog();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
