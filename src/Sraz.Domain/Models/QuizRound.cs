namespace Sraz.Domain.Models;

public sealed class QuizRound
{
    public IReadOnlyList<Question> Questions { get; init; } = Array.Empty<Question>();
    public int CurrentIndex { get; private set; }
    public int Score { get; private set; }

    public Question? CurrentQuestion => CurrentIndex < Questions.Count ? Questions[CurrentIndex] : null;

    public bool SubmitAnswer(int selectedIndex)
    {
        var question = CurrentQuestion;
        if (question is null)
        {
            return false;
        }

        var isCorrect = selectedIndex == question.CorrectOptionIndex;
        Score += isCorrect ? 100 : -25;
        CurrentIndex++;
        return isCorrect;
    }

    public bool HasNextQuestion => CurrentIndex < Questions.Count;
}
