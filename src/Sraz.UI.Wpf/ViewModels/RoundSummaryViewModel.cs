using Sraz.Domain.Models;
using Sraz.UI.Wpf.Localization;

namespace Sraz.UI.Wpf.ViewModels;

public sealed class RoundSummaryViewModel : BindableBase
{
    public RoundSummaryViewModel(QuizRound round)
    {
        var loc = Localizer.Instance;

        FinalScoreText = loc.Format("FinalScore", round.Score);
        QuestionsPlayedText = loc.Format("QuestionsPlayed", round.CurrentIndex);
        CorrectAnswersText = loc.Format("CorrectAnswers", round.CorrectAnswers);
        WrongAnswersText = loc.Format("WrongAnswers", round.WrongAnswers);
        TimeoutsText = loc.Format("Timeouts", round.Timeouts);
        BestStreakText = loc.Format("BestStreak", round.BestStreak);
        ModeText = loc.Format("ModeLabel", loc.ModeLabel(round.Mode.ToString()));
    }

    public string FinalScoreText { get; }
    public string QuestionsPlayedText { get; }
    public string CorrectAnswersText { get; }
    public string WrongAnswersText { get; }
    public string TimeoutsText { get; }
    public string BestStreakText { get; }
    public string ModeText { get; }
}
