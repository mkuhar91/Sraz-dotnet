using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Sraz.Domain.Models;
using Sraz.UI.Wpf.Localization;

namespace Sraz.UI.Wpf.ViewModels;

public sealed class OptionItem
{
    public OptionItem(int index, string text)
    {
        Index = index;
        Text = text;
    }

    public int Index { get; }
    public string Text { get; }
}

public sealed class QuestionViewModel : BindableBase, IDisposable
{
    private const int QuestionSeconds = 20;
    private static readonly TimeSpan FeedbackDelay = TimeSpan.FromMilliseconds(1500);

    private readonly QuizRound _round;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _secondsLeft;
    private bool _canAnswer;
    private bool _hasQuestion = true;
    private string _questionText = string.Empty;
    private string _header = string.Empty;
    private string _footer = string.Empty;
    private string _feedbackTitle = string.Empty;
    private string _feedbackExplanation = string.Empty;
    private bool _feedbackVisible;
    private string _textAnswer = string.Empty;
    private bool _disposed;

    public QuestionViewModel(QuizRound round)
    {
        _round = round;
        _secondsLeft = round.TimeLimitSeconds ?? QuestionSeconds;
        _timer.Tick += (_, _) => OnTick();
        LoadCurrent();
    }

    public ObservableCollection<OptionItem> Options { get; } = new();

    public bool HasQuestion
    {
        get => _hasQuestion;
        private set => SetProperty(ref _hasQuestion, value);
    }

    public bool CanAnswer
    {
        get => _canAnswer;
        private set
        {
            if (SetProperty(ref _canAnswer, value))
            {
                RaisePropertyChanged(nameof(CanUseFiftyFifty));
                RaisePropertyChanged(nameof(CanUseExtraTime));
                RaisePropertyChanged(nameof(CanSkip));
            }
        }
    }

    public string QuestionText
    {
        get => _questionText;
        private set => SetProperty(ref _questionText, value);
    }

    public string Header
    {
        get => _header;
        private set => SetProperty(ref _header, value);
    }

    public string Footer
    {
        get => _footer;
        private set => SetProperty(ref _footer, value);
    }

    public string TimeLeftText => Localizer.Instance.Format("TimeLeft", _secondsLeft);

    public string FeedbackTitle
    {
        get => _feedbackTitle;
        private set => SetProperty(ref _feedbackTitle, value);
    }

    public string FeedbackExplanation
    {
        get => _feedbackExplanation;
        private set => SetProperty(ref _feedbackExplanation, value);
    }

    public Visibility FeedbackVisible => _feedbackVisible ? Visibility.Visible : Visibility.Collapsed;

    public string? ImagePath { get; private set; }

    public Visibility ImageVisible => ImagePath is null ? Visibility.Collapsed : Visibility.Visible;

    public bool IsTextInput => _round.CurrentQuestion?.Type == QuestionType.TextInput;

    public Visibility TextInputVisible => IsTextInput ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OptionsVisible => IsTextInput ? Visibility.Collapsed : Visibility.Visible;

    public string TextAnswer
    {
        get => _textAnswer;
        set => SetProperty(ref _textAnswer, value);
    }

    public bool CanUseFiftyFifty => CanAnswer && _round.CanUseFiftyFifty;

    public bool CanUseExtraTime => CanAnswer && _round.CanUseExtraTime;

    public bool CanSkip => CanAnswer && _round.CanSkip;

    public string FiftyFiftyLabel => Localizer.Instance.Get("Lifeline5050");

    public string ExtraTimeLabel => Localizer.Instance.Get("LifelineTime");

    public string SkipLabel => Localizer.Instance.Get("LifelineSkip");

    public string SubmitLabel => Localizer.Instance.Get("Submit");

    public string TypeAnswerLabel => Localizer.Instance.Get("TypeAnswer");

    public void StartTimer() => _timer.Start();

    public Task SelectAnswerAsync(OptionItem option)
    {
        if (!CanAnswer)
        {
            return Task.CompletedTask;
        }

        return ResolveAsync(q => _round.SubmitAnswer(option.Index), false);
    }

    public Task SubmitTextAsync()
    {
        if (!CanAnswer || !IsTextInput)
        {
            return Task.CompletedTask;
        }

        return ResolveAsync(q => _round.SubmitTextAnswer(TextAnswer), false);
    }

    public void UseFiftyFifty()
    {
        if (!CanUseFiftyFifty)
        {
            return;
        }

        foreach (var index in _round.UseFiftyFifty())
        {
            var item = Options.FirstOrDefault(o => o.Index == index);
            if (item is not null)
            {
                Options.Remove(item);
            }
        }

        RefreshLifelines();
    }

    public void UseExtraTime()
    {
        if (!CanUseExtraTime || !_round.UseExtraTime())
        {
            return;
        }

        _secondsLeft += QuizRound.ExtraTimeSeconds;
        RaisePropertyChanged(nameof(TimeLeftText));
        RefreshLifelines();
    }

    public async Task SkipAsync()
    {
        if (!CanSkip || !_round.Skip())
        {
            return;
        }

        CanAnswer = false;
        await Task.Yield();
        LoadCurrent();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
    }

    private async Task ResolveAsync(Func<Question, bool> submit, bool timedOut)
    {
        var question = _round.CurrentQuestion;
        if (question is null)
        {
            return;
        }

        CanAnswer = false;
        var loc = Localizer.Instance;
        var correct = submit(question);

        FeedbackTitle = correct ? loc.Get("Correct") : loc.Get("Wrong");
        ShowFeedback(question, correct);
        await FinishAsync();
    }

    private void ShowFeedback(Question question, bool correct)
    {
        var loc = Localizer.Instance;
        FeedbackExplanation = correct
            ? question.Explanation
            : $"{loc.Format("CorrectAnswerIs", question.CorrectAnswerText)} {question.Explanation}".Trim();
        _feedbackVisible = true;
        RaisePropertyChanged(nameof(FeedbackVisible));
        UpdateFooter();
    }

    private async Task FinishAsync()
    {
        _timer.Stop();
        await Task.Delay(FeedbackDelay);
        if (_disposed)
        {
            return;
        }

        LoadCurrent();
        if (HasQuestion)
        {
            _timer.Start();
        }
    }

    private void OnTick()
    {
        if (_round.TimeLimitSeconds is not null)
        {
            _secondsLeft--;
            RaisePropertyChanged(nameof(TimeLeftText));
            if (_secondsLeft <= 0)
            {
                _timer.Stop();
                _round.End();
                CanAnswer = false;
                LoadCurrent();
            }

            return;
        }

        if (!CanAnswer)
        {
            return;
        }

        _secondsLeft--;
        RaisePropertyChanged(nameof(TimeLeftText));
        if (_secondsLeft <= 0)
        {
            _ = TimeoutAsync();
        }
    }

    private async Task TimeoutAsync()
    {
        var question = _round.CurrentQuestion;
        if (question is null || !CanAnswer)
        {
            return;
        }

        CanAnswer = false;
        _round.RegisterTimeout();
        FeedbackTitle = Localizer.Instance.Get("TimesUp");
        ShowFeedback(question, false);
        await FinishAsync();
    }

    private void LoadCurrent()
    {
        _feedbackVisible = false;
        RaisePropertyChanged(nameof(FeedbackVisible));

        var question = _round.CurrentQuestion;
        if (question is null)
        {
            HasQuestion = false;
            CanAnswer = false;
            UpdateFooter();
            return;
        }

        var loc = Localizer.Instance;
        QuestionText = question.Text;
        Header = loc.Format("QuestionHeader", loc.CategoryLabel(question.Category), loc.DifficultyLabel(DifficultyName(question.Difficulty)));
        ImagePath = question.ImagePath;
        TextAnswer = string.Empty;

        Options.Clear();
        for (var i = 0; i < question.Options.Count; i++)
        {
            Options.Add(new OptionItem(i, question.Options[i]));
        }

        if (_round.TimeLimitSeconds is null)
        {
            _secondsLeft = QuestionSeconds;
        }

        RaisePropertyChanged(nameof(TimeLeftText));
        RaisePropertyChanged(nameof(ImagePath));
        RaisePropertyChanged(nameof(ImageVisible));
        RaisePropertyChanged(nameof(IsTextInput));
        RaisePropertyChanged(nameof(TextInputVisible));
        RaisePropertyChanged(nameof(OptionsVisible));
        CanAnswer = true;
        UpdateFooter();
    }

    private void RefreshLifelines()
    {
        RaisePropertyChanged(nameof(CanUseFiftyFifty));
        RaisePropertyChanged(nameof(CanUseExtraTime));
        RaisePropertyChanged(nameof(CanSkip));
    }

    private void UpdateFooter()
    {
        var loc = Localizer.Instance;
        var multiplier = _round.ScoreMultiplier.ToString("0.#");
        var number = Math.Min(_round.CurrentIndex + 1, Math.Max(_round.Questions.Count, 1));

        var text = _round.Mode == GameMode.Classic || _round.Mode == GameMode.Daily
            ? loc.Format("FooterFixed", number, _round.Questions.Count, _round.Score, _round.Streak, multiplier,
                _round.CorrectAnswers, _round.WrongAnswers, _round.Timeouts)
            : loc.Format("FooterOpen", number, _round.Score, _round.Streak, multiplier,
                _round.CorrectAnswers, _round.WrongAnswers, _round.Timeouts);

        if (_round.LivesLeft is int lives)
        {
            text += loc.Format("FooterLives", lives);
        }

        Footer = text;
    }

    private static string DifficultyName(int difficulty) => difficulty switch
    {
        1 => "Easy",
        2 => "Medium",
        _ => "Hard"
    };
}
