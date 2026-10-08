using System.Collections.ObjectModel;
using Sraz.Domain.Models;

namespace Sraz.UI.Wpf.ViewModels;

public sealed class QuestionViewModel
{
    private readonly QuizRound _round;

    public QuestionViewModel(QuizRound round)
    {
        _round = round;
        Options = new ObservableCollection<string>();
        LoadCurrent();
    }

    public string Header { get; private set; } = string.Empty;
    public string QuestionText { get; private set; } = string.Empty;
    public ObservableCollection<string> Options { get; }
    public string Footer { get; private set; } = string.Empty;
    public int Score => _round.Score;
    public bool HasQuestion => _round.CurrentQuestion is not null;

    public void SelectAnswer(string selectedOption)
    {
        var current = _round.CurrentQuestion;
        if (current is null)
        {
            return;
        }

        var selectedIndex = current.Options.ToList().IndexOf(selectedOption);
        _round.SubmitAnswer(selectedIndex);
        LoadCurrent();
    }

    private void LoadCurrent()
    {
        var current = _round.CurrentQuestion;
        if (current is null)
        {
            Header = "Round complete";
            QuestionText = "";
            Options.Clear();
            Footer = $"Score: {_round.Score}";
            return;
        }

        Header = $"Category: {current.Category} | Difficulty: {current.Difficulty}";
        QuestionText = current.Text;
        Options.Clear();
        foreach (var option in current.Options)
        {
            Options.Add(option);
        }

        Footer = $"Question {_round.CurrentIndex + 1}/{_round.Questions.Count} | Score: {_round.Score}";
    }
}
