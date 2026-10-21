namespace Sraz.Domain.Models;

public sealed class QuizRound
{
    public const int CorrectAnswerPoints = 100;
    public const int WrongAnswerPenalty = 25;
    public const int TimeoutPenalty = 40;
    public const int SurvivalMistakes = 3;
    public const int TimeAttackSeconds = 60;
    public const int ExtraTimeSeconds = 10;

    private readonly HashSet<Lifeline> _usedLifelines = new();
    private bool _ended;

    public IReadOnlyList<Question> Questions { get; init; } = Array.Empty<Question>();
    public GameMode Mode { get; init; } = GameMode.Classic;
    public int MaxMistakes { get; init; }
    public int? TimeLimitSeconds { get; init; }

    public int CurrentIndex { get; private set; }
    public int Score { get; private set; }
    public int CorrectAnswers { get; private set; }
    public int WrongAnswers { get; private set; }
    public int Timeouts { get; private set; }
    public int Skipped { get; private set; }
    public int Streak { get; private set; }
    public int BestStreak { get; private set; }

    public bool IsFinished =>
        _ended
        || CurrentIndex >= Questions.Count
        || (MaxMistakes > 0 && WrongAnswers >= MaxMistakes);

    public Question? CurrentQuestion => IsFinished ? null : Questions[CurrentIndex];

    public bool HasNextQuestion => !IsFinished;

    public int? LivesLeft => MaxMistakes > 0 ? Math.Max(0, MaxMistakes - WrongAnswers) : null;

    public double ScoreMultiplier => Streak >= 5 ? 2.0 : Streak >= 3 ? 1.5 : 1.0;

    public bool SubmitAnswer(int selectedIndex)
    {
        var question = CurrentQuestion;
        if (question is null)
        {
            return false;
        }

        return Resolve(selectedIndex == question.CorrectOptionIndex);
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
        Streak = 0;
        Score = Math.Max(0, Score - TimeoutPenalty);
        CurrentIndex++;
    }

    public void End()
    {
        _ended = true;
    }

    public bool IsLifelineUsed(Lifeline lifeline) => _usedLifelines.Contains(lifeline);

    public bool CanUseFiftyFifty =>
        !IsLifelineUsed(Lifeline.FiftyFifty)
        && CurrentQuestion is { Type: QuestionType.MultipleChoice, Options.Count: >= 3 };

    public bool CanUseExtraTime => !IsLifelineUsed(Lifeline.ExtraTime) && !IsFinished;

    public bool CanSkip =>
        !IsLifelineUsed(Lifeline.Skip)
        && !IsFinished
        && CurrentIndex + 1 < Questions.Count;

    public IReadOnlyList<int> UseFiftyFifty()
    {
        if (!CanUseFiftyFifty)
        {
            return Array.Empty<int>();
        }

        var question = CurrentQuestion!;
        var removed = Enumerable.Range(0, question.Options.Count)
            .Where(i => i != question.CorrectOptionIndex)
            .OrderBy(_ => Random.Shared.Next())
            .Take(Math.Min(2, question.Options.Count - 2))
            .ToList();

        _usedLifelines.Add(Lifeline.FiftyFifty);
        return removed;
    }

    public bool UseExtraTime()
    {
        if (!CanUseExtraTime)
        {
            return false;
        }

        _usedLifelines.Add(Lifeline.ExtraTime);
        return true;
    }

    public bool Skip()
    {
        if (!CanSkip)
        {
            return false;
        }

        _usedLifelines.Add(Lifeline.Skip);
        Skipped++;
        CurrentIndex++;
        return true;
    }

    private bool Resolve(bool isCorrect)
    {
        if (isCorrect)
        {
            CorrectAnswers++;
            Streak++;
            BestStreak = Math.Max(BestStreak, Streak);
            Score += (int)Math.Round(CorrectAnswerPoints * ScoreMultiplier);
        }
        else
        {
            WrongAnswers++;
            Streak = 0;
            Score = Math.Max(0, Score - WrongAnswerPenalty);
        }

        CurrentIndex++;
        return isCorrect;
    }
}
