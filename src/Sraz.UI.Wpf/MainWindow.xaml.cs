using System.IO;
using System.Windows;
using Sraz.Application.Services;
using Sraz.Infrastructure.Persistence;
using Sraz.UI.Wpf.ViewModels;

namespace Sraz.UI.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        var dataPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "questions.seed.json"));
        var provider = new JsonQuestionProvider(dataPath);
        var roundService = new QuizRoundService(provider);
        var round = await roundService.CreateRoundAsync(10);

        if (round.Questions.Count == 0)
        {
            MessageBox.Show("No questions found. Please seed data/questions.seed.json.", "Sraz", MessageBoxButton.OK, MessageBoxImage.Warning);
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
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
