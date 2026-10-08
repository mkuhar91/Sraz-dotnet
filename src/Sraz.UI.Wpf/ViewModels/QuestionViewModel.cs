using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Sraz.Domain.Models;

namespace Sraz.UI.Wpf.ViewModels;

public sealed class QuestionViewModel : BindableBase, IDisposable
{
    private readonly QuizRound _round;
    private readonly DispatcherTimer _timer;
    private int _timeLeftSeconds;
    private string _header = string.Empty;
    private string _questionText = string.Empty;
    private string _footer = string.Empty;
    private string _feedbackTitle = string.Empty;
    private string _feedbackExplanation = string.Empty;
    private Visibility _feedbackVisible = Visibility.Collapsed;
    private bool _canAnswer = true;

    public QuestionViewModel(QuizRound round)
    {
        _round = round;
        Options = new ObservableCollection<string>();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += TimerOnTick;
        LoadCurrent();
    }

    public string Header
    {
        get => _header;
        private set => SetProperty(ref _header, value);
    }

    public string QuestionText
    {
        get => _questionText;
        private set => SetProperty(ref _questionText, value);
    }

    public ObservableCollection<string> Options { get; }

    public string Footer
    {
        get => _footer;
        private set => SetProperty(ref _footer, value);
    }

    public string TimeLeftText => $"Time: {_timeLeftSeconds}s";

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

    public Visibility FeedbackVisible
    {
        get => _feedbackVisible;
        private set => SetProperty(ref _feedbackVisible, value);
    }

    public bool CanAnswer
    {
        get => _canAnswer;
        private set => SetProperty(ref _canAnswer, value);
    }

    public int Score => _round.Score;
    public bool HasQuestion => _round.CurrentQuestion is not null;

    public void StartTimer()
    {
        ResetTimer();
    }

    public async Task SelectAnswerAsync(string selectedOption)
    {
        var current = _round.CurrentQuestion;
        if (current is null || !CanAnswer)
        {
            return;
        }

        _timer.Stop();
        CanAnswer = false;

        var selectedIndex = current.Options.ToList().IndexOf(selectedOption);
        var isCorrect = _round.SubmitAnswer(selectedIndex);

        FeedbackTitle = isCorrect ? "Correct!" : "Wrong!";
        FeedbackExplanation = current.Explanation;
        FeedbackVisible = Visibility.Visible;
        UpdateFooter();

        await Task.Delay(1200);

        FeedbackVisible = Visibility.Collapsed;
        LoadCurrent();
        if (HasQuestion)
        {
            CanAnswer = true;
            ResetTimer();
        }
    }

    private async void TimerOnTick(object? sender, EventArgs e)
    {
        _timeLeftSeconds--;
        RaisePropertyChanged(nameof(TimeLeftText));

        if (_timeLeftSeconds > 0)
        {
            return;
        }

        _timer.Stop();
        var current = _round.CurrentQuestion;
        if (current is null)
        {
            return;
        }

        CanAnswer = false;
        _round.RegisterTimeout();

        FeedbackTitle = "Time's up!";
        FeedbackExplanation = current.Explanation;
        FeedbackVisible = Visibility.Visible;
        UpdateFooter();

        await Task.Delay(1200);

        FeedbackVisible = Visibility.Collapsed;
        LoadCurrent();
        if (HasQuestion)
        {
            CanAnswer = true;
            ResetTimer();
        }
    }

    private void LoadCurrent()
    {
        var current = _round.CurrentQuestion;
        if (current is null)
        {
            Header = "Round complete";
            QuestionText = string.Empty;
            Options.Clear();
            Footer = $"Score: {_round.Score}";
            _timer.Stop();
            RaisePropertyChanged(nameof(TimeLeftText));
            return;
        }

        Header = $"Category: {current.Category} | Difficulty: {current.Difficulty}";
        QuestionText = current.Text;
        Options.Clear();
        foreach (var option in current.Options)
        {
            Options.Add(option);
        }

        UpdateFooter();
    }

    private void UpdateFooter()
    {
        Footer = $"Question {_round.CurrentIndex + 1}/{_round.Questions.Count} | Score: {_round.Score} | Correct: {_round.CorrectAnswers} | Wrong: {_round.WrongAnswers} | Timeouts: {_round.Timeouts}";
    }

    private void ResetTimer()
    {
        _timeLeftSeconds = 20;
        RaisePropertyChanged(nameof(TimeLeftText));
        _timer.Stop();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= TimerOnTick;
    }
}
