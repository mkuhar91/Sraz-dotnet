using System.Windows;
using Sraz.Application.Services;
using Sraz.Infrastructure.Persistence;
using Sraz.UI.Wpf.Localization;
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
        try
        {
            await StartQuizAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Localizer.Instance.Format("MsgError", ex.Message), "Sraz", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StartQuizAsync()
    {
        var loc = Localizer.Instance;
        var playerName = _setupViewModel.PlayerName.Trim();
        if (string.IsNullOrWhiteSpace(playerName))
        {
            MessageBox.Show(loc.Get("MsgNameRequired"), "Sraz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var provider = new JsonQuestionProvider(AppPaths.QuestionsFile);
        var roundService = new QuizRoundService(provider);
        var round = await roundService.CreateRoundAsync(
            10, _setupViewModel.SelectedCategory, _setupViewModel.SelectedDifficulty, _setupViewModel.Mode);

        if (round.Questions.Count == 0)
        {
            MessageBox.Show(loc.Get("MsgNoQuestions"), "Sraz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var vm = new QuestionViewModel(round);
        var window = new QuestionWindow(vm) { Owner = this };
        window.ShowDialog();

        if (!round.IsFinished)
        {
            return;
        }

        var summary = new RoundSummaryWindow(new RoundSummaryViewModel(round)) { Owner = this };
        summary.ShowDialog();

        var leaderboardViewModel = new LeaderboardViewModel(
            new LeaderboardService(new LeaderboardStore(AppPaths.LeaderboardFile)));

        leaderboardViewModel.SaveCurrentGame(
            playerName,
            round,
            _setupViewModel.SelectedCategory,
            _setupViewModel.SelectedDifficulty);
    }

    private void Leaderboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var viewModel = new LeaderboardViewModel(new LeaderboardService(new LeaderboardStore(AppPaths.LeaderboardFile)));
            var leaderboardWindow = new LeaderboardWindow(viewModel) { Owner = this };
            leaderboardWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Localizer.Instance.Format("MsgLeaderboardError", ex.Message), "Sraz", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
