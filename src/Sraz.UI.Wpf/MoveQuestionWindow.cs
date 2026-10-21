using System.Windows;
using System.Windows.Controls;
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

        Content = content;
    }

    public bool IsAnswerCorrect { get; private set; }

    private void Submit(bool correct)
    {
        IsAnswerCorrect = correct;
        DialogResult = true;
    }
}
