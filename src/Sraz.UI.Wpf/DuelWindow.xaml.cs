using System.Windows;
using System.Windows.Controls;
using Sraz.Domain.Models;
using Sraz.UI.Wpf.Localization;

namespace Sraz.UI.Wpf;

public partial class DuelWindow : Window
{
    private readonly IReadOnlyList<Question> _questions;
    private readonly List<bool> _results = new();
    private int _questionIndex;
    private bool _completed;

    public DuelWindow(IReadOnlyList<Question> questions)
    {
        InitializeComponent();
        if (questions.Count != 3 || questions.Any(question => question.Type != QuestionType.MultipleChoice))
        {
            throw new ArgumentException("A duel requires three multiple-choice questions.", nameof(questions));
        }

        _questions = questions;
        Title = Localizer.Instance.Get("DuelTitle");
        Closing += (_, _) =>
        {
            if (!_completed && !_results.Contains(false))
            {
                _results.Add(false);
            }
        };
        DisplayQuestion();
    }

    public IReadOnlyList<bool> Results => _results;

    private void DisplayQuestion()
    {
        var question = _questions[_questionIndex];
        if (Enum.TryParse<QuestionTheme>(question.Category, true, out var theme))
        {
            ThemeText.Text = ThemeColors.Label(theme);
        }
        ProgressText.Text = Localizer.Instance.Format("DuelProgress", _questionIndex + 1, _questions.Count);
        QuestionText.Text = question.Text;
        FeedbackText.Text = string.Empty;
        AnswerPanel.Children.Clear();

        for (var index = 0; index < question.Options.Count; index++)
        {
            var optionIndex = index;
            var button = new Button
            {
                Content = question.Options[index],
                Height = 40,
                Margin = new Thickness(0, 0, 0, 7)
            };
            button.Click += (_, _) => Submit(optionIndex == question.CorrectOptionIndex);
            AnswerPanel.Children.Add(button);
        }
    }

    private void Submit(bool correct)
    {
        _results.Add(correct);
        if (!correct)
        {
            _completed = true;
            FeedbackText.Text = Localizer.Instance.Get("DuelFailed");
            DialogResult = false;
            return;
        }

        _questionIndex++;
        if (_questionIndex == _questions.Count)
        {
            _completed = true;
            DialogResult = true;
            return;
        }

        DisplayQuestion();
    }
}
