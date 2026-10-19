using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sraz.Domain.Models;
using Sraz.UI.Wpf.Localization;

namespace Sraz.UI.Wpf;

public sealed class MoveQuestionWindow : Window
{
    public MoveQuestionWindow(Question question)
    {
        Title = Localizer.Instance.Get("WindowQuestion");
        Width = 640;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var content = new StackPanel { Margin = new Thickness(22) };
        if (Enum.TryParse<QuestionTheme>(question.Category, true, out var theme))
        {
            content.Children.Add(new TextBlock
            {
                Text = ThemeColors.Label(theme),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        content.Children.Add(new TextBlock
        {
            Text = question.Text,
            FontSize = 23,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });

        if (question.Type == QuestionType.TextInput)
        {
            var answer = new TextBox { Height = 38, FontSize = 18, Margin = new Thickness(0, 0, 0, 8) };
            var submit = new Button { Content = Localizer.Instance.Get("Submit"), Height = 38 };
            submit.Click += (_, _) => Submit(question.IsTextAnswerCorrect(answer.Text));
            answer.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Submit(question.IsTextAnswerCorrect(answer.Text));
                }
            };
            content.Children.Add(answer);
            content.Children.Add(submit);
            Loaded += (_, _) => answer.Focus();
        }
        else
        {
            foreach (var (option, index) in question.Options.Select((option, index) => (option, index)))
            {
                var optionIndex = index;
                var button = new Button
                {
                    Content = option,
                    Height = 40,
                    Margin = new Thickness(0, 0, 0, 7)
                };
                button.Click += (_, _) => Submit(optionIndex == question.CorrectOptionIndex);
                content.Children.Add(button);
            }
        }

        Content = content;
    }

    public bool IsAnswerCorrect { get; private set; }

    private void Submit(bool correct)
    {
        IsAnswerCorrect = correct;
        DialogResult = true;
    }
}
