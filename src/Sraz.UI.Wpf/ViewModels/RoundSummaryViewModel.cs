namespace Sraz.UI.Wpf.ViewModels;

public sealed class RoundSummaryViewModel : BindableBase
{
    public RoundSummaryViewModel(int score, int totalQuestions, int correctAnswers, int wrongAnswers, int timeouts)
    {
        Score = score;
        TotalQuestions = totalQuestions;
        CorrectAnswers = correctAnswers;
        WrongAnswers = wrongAnswers;
        Timeouts = timeouts;
    }

    public int Score { get; }
    public int TotalQuestions { get; }
    public int CorrectAnswers { get; }
    public int WrongAnswers { get; }
    public int Timeouts { get; }
}
