namespace Sraz.Domain.Models;

public sealed class QuizRound
{
    public IReadOnlyList<Question> Questions { get; init; } = Array.Empty<Question>();
    public int CurrentIndex { get; private set; }
    public int Score { get; private set; }
    public int CorrectAnswers { get; private set; }
    public int WrongAnswers { get; private set; }
    public int Timeouts { get; private set; }

    public Question? CurrentQuestion => CurrentIndex < Questions.Count ? Questions[CurrentIndex] : null;

    public bool SubmitAnswer(int selectedIndex)
    {
        var question = CurrentQuestion;
        if (question is null)
        {
            return false;
        }

        var isCorrect = selectedIndex == question.CorrectOptionIndex;
        if (isCorrect)
        {
            CorrectAnswers++;
            Score += 100;
        }
        else
        {
            WrongAnswers++;
            Score -= 25;
        }

        CurrentIndex++;
        return isCorrect;
    }

    public void RegisterTimeout()
    {
        var question = CurrentQuestion;
        if (question is null)
        {
            return;
        }

        Timeouts++;
        WrongAnswers++;
        Score -= 40;
        CurrentIndex++;
    }

    public bool HasNextQuestion => CurrentIndex < Questions.Count;
}
